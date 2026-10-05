using System.Security.Cryptography;
using System.Text.Json;
using BusinessOS.POS.Application.Abstractions.Authentication;
using BusinessOS.POS.Application.Abstractions.Backup;
using BusinessOS.POS.Application.Abstractions.Persistence;
using BusinessOS.POS.Application.Abstractions.Storage;
using Microsoft.Data.Sqlite;

namespace BusinessOS.POS.Persistence;

public sealed class LocalBackupService(
    IApplicationPaths paths,
    IPermissionAuthorizer permissions,
    ILocalDatabaseInitializer initializer) : ILocalBackupService
{
    private const int ManifestVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<IReadOnlyList<LocalBackupSnapshot>> ListAsync(CancellationToken cancellationToken = default)
    {
        permissions.Demand("settings.manage");
        paths.EnsureCreated();
        Directory.CreateDirectory(paths.BackupsDirectory);

        var rows = new List<LocalBackupSnapshot>();
        foreach (var path in Directory.EnumerateFiles(paths.BackupsDirectory, "businessos-pos-*.db"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { rows.Add(await DescribeAsync(path, requireManifest: true, cancellationToken)); }
            catch { }
        }

        return rows.OrderByDescending(x => x.CreatedAt).ToList();
    }

    public async Task<LocalBackupSnapshot> CreateAsync(
        string kind = "manual",
        CancellationToken cancellationToken = default)
    {
        permissions.Demand("settings.manage");
        ValidateKind(kind);
        await _gate.WaitAsync(cancellationToken);
        try { return await CreateInternalAsync(kind, cancellationToken); }
        finally { _gate.Release(); }
    }

    public async Task<LocalBackupVerification> VerifyAsync(
        string backupPath,
        CancellationToken cancellationToken = default)
    {
        permissions.Demand("settings.manage");
        try
        {
            var backup = await DescribeAsync(backupPath, requireManifest: true, cancellationToken);
            return new(true, "Backup integrity and checksum are valid.", backup);
        }
        catch (Exception exception)
        {
            return new(false, exception.Message, null);
        }
    }

    public async Task<LocalRestoreResult> RestoreAsync(
        string backupPath,
        CancellationToken cancellationToken = default)
    {
        permissions.Demand("settings.manage");
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var candidate = await DescribeAsync(backupPath, requireManifest: true, cancellationToken);
            LocalBackupSnapshot? safety = File.Exists(paths.DatabasePath)
                ? await CreateInternalAsync("pre-restore", cancellationToken)
                : null;

            var stage = Path.Combine(paths.RootPath, $".restore-stage-{Guid.NewGuid():N}.db");
            var rollback = Path.Combine(paths.RootPath, $".restore-rollback-{Guid.NewGuid():N}.db");
            await CopyFileAsync(candidate.FullPath, stage, cancellationToken);

            var staged = await DescribeAsync(stage, requireManifest: false, cancellationToken);
            if (!string.Equals(staged.Sha256, candidate.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The staged restore copy failed checksum verification.");

            var hadOriginal = File.Exists(paths.DatabasePath);
            var replaced = false;

            try
            {
                if (hadOriginal) await CheckpointAsync(paths.DatabasePath, cancellationToken);
                SqliteConnection.ClearAllPools();
                DeleteSidecars(paths.DatabasePath);

                if (hadOriginal)
                    File.Replace(stage, paths.DatabasePath, rollback, ignoreMetadataErrors: true);
                else
                    File.Move(stage, paths.DatabasePath);

                replaced = true;
                await initializer.InitializeAsync(cancellationToken);
                _ = await DescribeAsync(paths.DatabasePath, requireManifest: false, cancellationToken);

                if (File.Exists(rollback)) File.Delete(rollback);
                return new(candidate, safety, DateTimeOffset.UtcNow);
            }
            catch
            {
                SqliteConnection.ClearAllPools();
                DeleteSidecars(paths.DatabasePath);

                if (replaced)
                {
                    if (hadOriginal && File.Exists(rollback))
                        File.Copy(rollback, paths.DatabasePath, overwrite: true);
                    else if (!hadOriginal && File.Exists(paths.DatabasePath))
                        File.Delete(paths.DatabasePath);
                }

                if (hadOriginal && File.Exists(paths.DatabasePath))
                    await initializer.InitializeAsync(cancellationToken);

                throw;
            }
            finally
            {
                if (File.Exists(stage)) File.Delete(stage);
                if (File.Exists(rollback)) File.Delete(rollback);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<LocalBackupSnapshot> CreateInternalAsync(string kind, CancellationToken cancellationToken)
    {
        if (!File.Exists(paths.DatabasePath))
            throw new InvalidOperationException("The local POS database does not exist yet.");

        paths.EnsureCreated();
        Directory.CreateDirectory(paths.BackupsDirectory);

        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmssfff");
        var fileName = $"businessos-pos-{kind}-{stamp}.db";
        var finalPath = Path.Combine(paths.BackupsDirectory, fileName);
        var tempPath = finalPath + ".partial";

        try
        {
            await CheckpointAsync(paths.DatabasePath, cancellationToken);

            await using var source = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = paths.DatabasePath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
            }.ToString());

            await using var destination = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = tempPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false,
            }.ToString());

            await source.OpenAsync(cancellationToken);
            await destination.OpenAsync(cancellationToken);
            source.BackupDatabase(destination);
            await destination.CloseAsync();
            await source.CloseAsync();

            var snapshot = await DescribeAsync(tempPath, requireManifest: false, cancellationToken);
            File.Move(tempPath, finalPath, overwrite: false);
            snapshot = snapshot with
            {
                FileName = fileName,
                FullPath = finalPath,
                CreatedAt = DateTimeOffset.UtcNow,
                Kind = kind,
                IsVerified = true,
            };

            await WriteManifestAsync(snapshot, cancellationToken);
            return snapshot;
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    private static async Task CheckpointAsync(string databasePath, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
        }.ToString());

        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
        _ = await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<LocalBackupSnapshot> DescribeAsync(
        string backupPath,
        bool requireManifest,
        CancellationToken cancellationToken)
    {
        backupPath = Path.GetFullPath(backupPath);
        if (!File.Exists(backupPath))
            throw new FileNotFoundException("Backup file was not found.", backupPath);

        await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = backupPath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString()))
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA integrity_check;";
            var integrity = Convert.ToString(await command.ExecuteScalarAsync(cancellationToken)) ?? string.Empty;
            if (!string.Equals(integrity, "ok", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"SQLite integrity check failed: {integrity}");
        }

        var info = new FileInfo(backupPath);
        var sha256 = await HashAsync(backupPath, cancellationToken);
        var createdAt = new DateTimeOffset(info.LastWriteTimeUtc);
        var kind = InferKind(info.Name);
        var manifestPath = backupPath + ".manifest.json";

        if (File.Exists(manifestPath))
        {
            await using var stream = File.OpenRead(manifestPath);
            var manifest = await JsonSerializer.DeserializeAsync<BackupManifest>(
                stream, JsonOptions, cancellationToken)
                ?? throw new InvalidOperationException("Backup manifest is invalid.");

            if (manifest.Version != ManifestVersion ||
                manifest.Length != info.Length ||
                !string.Equals(manifest.Sha256, sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Backup manifest does not match the backup file.");

            ValidateKind(manifest.Kind);
            createdAt = manifest.CreatedAt;
            kind = manifest.Kind;
        }
        else if (requireManifest)
        {
            throw new InvalidOperationException("Backup manifest is missing. Restore requires a verified BusinessOS POS backup.");
        }

        return new(info.Name, backupPath, createdAt, info.Length, sha256, kind, true);
    }

    private async Task WriteManifestAsync(LocalBackupSnapshot snapshot, CancellationToken cancellationToken)
    {
        var manifest = new BackupManifest(
            ManifestVersion,
            snapshot.CreatedAt,
            snapshot.Length,
            snapshot.Sha256,
            snapshot.Kind);

        var finalPath = snapshot.FullPath + ".manifest.json";
        var tempPath = finalPath + ".partial";

        await using (var stream = new FileStream(
            tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            81920, FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(stream, manifest, JsonOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        File.Move(tempPath, finalPath, overwrite: true);
    }

    private static async Task<string> HashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);

        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }

    private static async Task CopyFileAsync(string source, string destination, CancellationToken cancellationToken)
    {
        await using var input = new FileStream(
            source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(
            destination, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            81920, FileOptions.Asynchronous | FileOptions.WriteThrough);

        await input.CopyToAsync(output, cancellationToken);
        await output.FlushAsync(cancellationToken);
    }

    private static void DeleteSidecars(string databasePath)
    {
        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            var sidecar = databasePath + suffix;
            if (File.Exists(sidecar)) File.Delete(sidecar);
        }
    }

    private static string InferKind(string fileName) =>
        fileName.Contains("pre-restore", StringComparison.OrdinalIgnoreCase) ? "pre-restore" :
        fileName.Contains("pre-update", StringComparison.OrdinalIgnoreCase) ? "pre-update" :
        "manual";

    private static void ValidateKind(string kind)
    {
        if (kind is not ("manual" or "pre-restore" or "pre-update"))
            throw new ArgumentException("Unsupported backup kind.", nameof(kind));
    }

    private sealed record BackupManifest(
        int Version,
        DateTimeOffset CreatedAt,
        long Length,
        string Sha256,
        string Kind);
}
