using System.Buffers;
using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BusinessOS.POS.Application.Abstractions.Updates;

namespace BusinessOS.POS.Infrastructure.Updates;

public sealed class PosUpdateService : IPosUpdateService
{
    private readonly HttpClient _http;
    private readonly UpdateOptions _options;
    private readonly string _updatesRoot;

    public PosUpdateService(HttpClient http, UpdateOptions options, string updatesRoot)
    {
        _http = http;
        _options = options;
        _updatesRoot = Path.GetFullPath(updatesRoot);

        if (options.TimeoutSeconds is < 5 or > 300)
            throw new ArgumentOutOfRangeException(nameof(options.TimeoutSeconds));
        if (options.MaximumPackageBytes is < 1_048_576 or > 2_147_483_648L)
            throw new ArgumentOutOfRangeException(nameof(options.MaximumPackageBytes));

        _http.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        Directory.CreateDirectory(_updatesRoot);
    }

    public async Task<UpdateCheckResult> CheckAsync(
        Version currentVersion,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ManifestUrl))
            return new(UpdateAvailability.Blocked, currentVersion, null, null, "Update manifest URL is not configured.");
        if (string.IsNullOrWhiteSpace(_options.SigningPublicKeyPem))
            return new(UpdateAvailability.Blocked, currentVersion, null, null, "Trusted update signing key is not configured.");

        var manifestUri = RequireHttps(_options.ManifestUrl);
        using var response = await _http.GetAsync(manifestUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength is long manifestLength && manifestLength > 1_048_576)
            throw new InvalidOperationException("Update manifest is too large.");

        var manifest = await response.Content.ReadFromJsonAsync<UpdateManifest>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Update manifest is empty.");

        ValidateManifest(manifest);

        var available = Version.Parse(manifest.Version);
        var minimum = Version.Parse(manifest.MinimumSupportedVersion);

        if (available <= currentVersion)
            return new(UpdateAvailability.UpToDate, currentVersion, available, manifest, "BusinessOS POS is up to date.");

        var availability = currentVersion < minimum
            ? UpdateAvailability.Required
            : UpdateAvailability.Available;

        return new(
            availability,
            currentVersion,
            available,
            manifest,
            availability == UpdateAvailability.Required
                ? "This POS version is below the supported minimum and must be updated."
                : "A verified BusinessOS POS update is available.");
    }

    public async Task<PreparedUpdate> DownloadAndStageAsync(
        UpdateManifest manifest,
        CancellationToken cancellationToken = default)
    {
        ValidateManifest(manifest);

        var versionRoot = SafeChild(_updatesRoot, Version.Parse(manifest.Version).ToString());
        var downloadRoot = SafeChild(versionRoot, "download");
        var stagingRoot = SafeChild(versionRoot, "staging");
        Directory.CreateDirectory(downloadRoot);

        if (Directory.Exists(stagingRoot))
            Directory.Delete(stagingRoot, recursive: true);
        Directory.CreateDirectory(stagingRoot);

        var packagePath = SafeChild(downloadRoot, "businessos-pos-update.zip");
        var partialPath = packagePath + ".partial";
        if (File.Exists(partialPath)) File.Delete(partialPath);

        using (var response = await _http.GetAsync(
                   RequireHttps(manifest.PackageUrl),
                   HttpCompletionOption.ResponseHeadersRead,
                   cancellationToken))
        {
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long contentLength &&
                contentLength > _options.MaximumPackageBytes)
                throw new InvalidOperationException("Update package exceeds the configured size limit.");

            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var target = new FileStream(
                partialPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                81920, FileOptions.Asynchronous | FileOptions.WriteThrough);

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = ArrayPool<byte>.Shared.Rent(81920);
            long total = 0;

            try
            {
                while (true)
                {
                    var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                    if (read == 0) break;

                    total += read;
                    if (total > _options.MaximumPackageBytes)
                        throw new InvalidOperationException("Update package exceeds the configured size limit.");

                    hash.AppendData(buffer, 0, read);
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            await target.FlushAsync(cancellationToken);
            var actualHash = Convert.ToHexString(hash.GetHashAndReset());
            if (!string.Equals(actualHash, manifest.PackageSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Update package checksum verification failed.");
        }

        File.Move(partialPath, packagePath, overwrite: true);
        ExtractValidated(packagePath, stagingRoot);

        return new(manifest, packagePath, stagingRoot, DateTimeOffset.UtcNow);
    }

    public async Task<string> WriteApplyPlanAsync(
        PreparedUpdate prepared,
        string installationDirectory,
        string dataRootDirectory,
        int waitForProcessId,
        string? restartExecutable,
        string? preUpdateBackupPath,
        CancellationToken cancellationToken = default)
    {
        installationDirectory = Path.GetFullPath(installationDirectory);
        dataRootDirectory = Path.GetFullPath(dataRootDirectory);

        if (IsSameOrChild(dataRootDirectory, installationDirectory) ||
            IsSameOrChild(installationDirectory, dataRootDirectory))
            throw new InvalidOperationException("Application installation and POS data directories must be isolated.");

        if (!IsSameOrChild(prepared.StagingDirectory, _updatesRoot))
            throw new InvalidOperationException("Update staging directory is outside the trusted update workspace.");

        var versionRoot = SafeChild(_updatesRoot, Version.Parse(prepared.Manifest.Version).ToString());
        var rollbackDirectory = SafeChild(
            versionRoot,
            "rollback-" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff"));

        var plan = new UpdateApplyPlan(
            1,
            prepared.Manifest.Version,
            installationDirectory,
            dataRootDirectory,
            prepared.StagingDirectory,
            rollbackDirectory,
            waitForProcessId,
            restartExecutable,
            preUpdateBackupPath,
            DateTimeOffset.UtcNow);

        var planPath = SafeChild(versionRoot, "apply-plan.json");
        await File.WriteAllTextAsync(
            planPath,
            JsonSerializer.Serialize(plan, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }),
            cancellationToken);

        return planPath;
    }

    public Process LaunchApplyAgent(string planPath)
    {
        var exe = Path.Combine(AppContext.BaseDirectory, "Updater", "BusinessOS.POS.Updater.exe");
        if (!File.Exists(exe))
            throw new FileNotFoundException("BusinessOS POS updater agent is not installed.", exe);

        return Process.Start(new ProcessStartInfo(exe, $"--apply \"{planPath}\"")
        {
            UseShellExecute = true,
            Verb = OperatingSystem.IsWindows() ? "runas" : string.Empty,
        }) ?? throw new InvalidOperationException("Could not start the BusinessOS POS updater.");
    }

    private void ValidateManifest(UpdateManifest manifest)
    {
        if (manifest.SchemaVersion != 1)
            throw new InvalidOperationException("Unsupported update manifest schema.");
        if (!string.Equals(manifest.Channel, _options.Channel.ToString(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Update manifest channel does not match this installation.");

        _ = Version.Parse(manifest.Version);
        _ = Version.Parse(manifest.MinimumSupportedVersion);
        _ = RequireHttps(manifest.PackageUrl);

        if (manifest.PackageSha256.Length != 64 || manifest.PackageSha256.Any(c => !Uri.IsHexDigit(c)))
            throw new InvalidOperationException("Update package SHA-256 is invalid.");

        var payload = string.Join("\n",
            manifest.SchemaVersion,
            manifest.Channel,
            manifest.Version,
            manifest.MinimumSupportedVersion,
            manifest.PackageUrl,
            manifest.PackageSha256.ToUpperInvariant(),
            manifest.PublishedAt.ToUniversalTime().ToString("O"),
            manifest.ReleaseNotes ?? string.Empty);

        byte[] signature;
        try { signature = Convert.FromBase64String(manifest.Signature); }
        catch (FormatException) { throw new InvalidOperationException("Update manifest signature is invalid."); }

        using var rsa = RSA.Create();
        try { rsa.ImportFromPem(_options.SigningPublicKeyPem); }
        catch (Exception ex) { throw new InvalidOperationException("Trusted update public key is invalid.", ex); }

        if (!rsa.VerifyData(
                Encoding.UTF8.GetBytes(payload),
                signature,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pss))
            throw new InvalidOperationException("Update manifest signature verification failed.");
    }

    private void ExtractValidated(string packagePath, string stagingRoot)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        if (archive.Entries.Count > 20_000)
            throw new InvalidOperationException("Update package contains too many files.");

        long expandedBytes = 0;
        var expandedLimit = Math.Min(_options.MaximumPackageBytes * 4, 2_147_483_648L);

        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(name) || name.EndsWith('/')) continue;

            if (name.StartsWith('/') || name.Contains("../", StringComparison.Ordinal) || name.Contains(':'))
                throw new InvalidOperationException($"Unsafe update package path: {entry.FullName}");

            expandedBytes += entry.Length;
            if (expandedBytes > expandedLimit)
                throw new InvalidOperationException("Update package expands beyond the allowed maximum size.");

            var target = Path.GetFullPath(Path.Combine(stagingRoot, name));
            if (!IsSameOrChild(target, stagingRoot))
                throw new InvalidOperationException("Update package escapes the staging directory.");

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: false);
        }
    }

    private static Uri RequireHttps(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException("Update URLs must use absolute HTTPS URLs.");

        return uri;
    }

    private static string SafeChild(string root, string relative)
    {
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (!IsSameOrChild(path, root))
            throw new InvalidOperationException("Unsafe update workspace path.");
        return path;
    }

    internal static bool IsSameOrChild(string path, string root)
    {
        path = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return string.Equals(path, root, StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
