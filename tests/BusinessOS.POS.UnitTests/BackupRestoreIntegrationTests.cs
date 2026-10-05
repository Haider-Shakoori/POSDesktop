using BusinessOS.POS.Application.Abstractions.Authentication;
using BusinessOS.POS.Application.Abstractions.Backup;
using BusinessOS.POS.Application.Abstractions.Persistence;
using BusinessOS.POS.Application.Abstractions.Storage;
using BusinessOS.POS.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BusinessOS.POS.UnitTests;

public sealed class BackupRestoreIntegrationTests
{
    [Fact]
    public async Task Verified_backup_restore_recovers_database_and_creates_safety_copy()
    {
        var root = Path.Combine(Path.GetTempPath(), "BusinessOS-POS-backup-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var services = new ServiceCollection();
            services.AddSingleton<IApplicationPaths>(new TestPaths(root));
            services.AddBusinessOSPosPersistence();

            await using var provider = services.BuildServiceProvider();
            await provider.GetRequiredService<ILocalDatabaseInitializer>().InitializeAsync();
            await provider.GetRequiredService<IOwnerBootstrapService>()
                .CreateOwnerAsync("Original Owner", "owner", "Password-123", "en");
            await provider.GetRequiredService<IUserSessionService>()
                .LoginAsync("owner", "Password-123");

            var backupService = provider.GetRequiredService<ILocalBackupService>();
            var backup = await backupService.CreateAsync();

            var verification = await backupService.VerifyAsync(backup.FullPath);
            Assert.True(verification.IsValid);
            Assert.True(File.Exists(backup.FullPath + ".manifest.json"));

            await using (var connection = new SqliteConnection($"Data Source={Path.Combine(root, "test.db")}"))
            {
                await connection.OpenAsync();
                await using var update = connection.CreateCommand();
                update.CommandText = "UPDATE users SET Name = 'Changed Owner' WHERE NormalizedUsername = 'owner';";
                Assert.Equal(1, await update.ExecuteNonQueryAsync());
            }

            var restored = await backupService.RestoreAsync(backup.FullPath);
            Assert.NotNull(restored.SafetyBackup);
            Assert.Equal("pre-restore", restored.SafetyBackup!.Kind);

            await using (var connection = new SqliteConnection($"Data Source={Path.Combine(root, "test.db")}"))
            {
                await connection.OpenAsync();
                await using var query = connection.CreateCommand();
                query.CommandText = "SELECT Name FROM users WHERE NormalizedUsername = 'owner';";
                Assert.Equal("Original Owner", Convert.ToString(await query.ExecuteScalarAsync()));
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Modified_backup_is_rejected_by_manifest_checksum()
    {
        var root = Path.Combine(Path.GetTempPath(), "BusinessOS-POS-backup-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var services = new ServiceCollection();
            services.AddSingleton<IApplicationPaths>(new TestPaths(root));
            services.AddBusinessOSPosPersistence();

            await using var provider = services.BuildServiceProvider();
            await provider.GetRequiredService<ILocalDatabaseInitializer>().InitializeAsync();
            await provider.GetRequiredService<IOwnerBootstrapService>()
                .CreateOwnerAsync("Owner", "owner", "Password-123", "en");
            await provider.GetRequiredService<IUserSessionService>()
                .LoginAsync("owner", "Password-123");

            var backupService = provider.GetRequiredService<ILocalBackupService>();
            var backup = await backupService.CreateAsync();

            await using (var stream = new FileStream(backup.FullPath, FileMode.Append, FileAccess.Write, FileShare.None))
            {
                await stream.WriteAsync(new byte[] { 0x00 });
            }

            var verification = await backupService.VerifyAsync(backup.FullPath);
            Assert.False(verification.IsValid);
            Assert.Contains("manifest", verification.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private sealed class TestPaths(string root) : IApplicationPaths
    {
        public string RootPath { get; } = root;
        public string DatabasePath { get; } = Path.Combine(root, "test.db");
        public void EnsureCreated() => Directory.CreateDirectory(RootPath);
    }
}
