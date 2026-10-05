namespace BusinessOS.POS.Application.Abstractions.Backup;

public interface ILocalBackupService
{
    Task<IReadOnlyList<LocalBackupSnapshot>> ListAsync(CancellationToken cancellationToken = default);
    Task<LocalBackupSnapshot> CreateAsync(string kind = "manual", CancellationToken cancellationToken = default);
    Task<LocalBackupVerification> VerifyAsync(string backupPath, CancellationToken cancellationToken = default);
    Task<LocalRestoreResult> RestoreAsync(string backupPath, CancellationToken cancellationToken = default);
}

public sealed record LocalBackupSnapshot(
    string FileName,
    string FullPath,
    DateTimeOffset CreatedAt,
    long Length,
    string Sha256,
    string Kind,
    bool IsVerified);

public sealed record LocalBackupVerification(bool IsValid, string Message, LocalBackupSnapshot? Backup);

public sealed record LocalRestoreResult(
    LocalBackupSnapshot RestoredBackup,
    LocalBackupSnapshot? SafetyBackup,
    DateTimeOffset RestoredAt);
