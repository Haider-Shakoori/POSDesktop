using System.Diagnostics;
using System.Text.Json.Serialization;

namespace BusinessOS.POS.Application.Abstractions.Updates;

public enum UpdateChannel { Stable = 0 }

public sealed record UpdateOptions(
    string ManifestUrl,
    string SigningPublicKeyPem,
    UpdateChannel Channel = UpdateChannel.Stable,
    int TimeoutSeconds = 30,
    long MaximumPackageBytes = 536_870_912);

public sealed record UpdateManifest(
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("channel")] string Channel,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("minimum_supported_version")] string MinimumSupportedVersion,
    [property: JsonPropertyName("package_url")] string PackageUrl,
    [property: JsonPropertyName("package_sha256")] string PackageSha256,
    [property: JsonPropertyName("published_at")] DateTimeOffset PublishedAt,
    [property: JsonPropertyName("release_notes")] string? ReleaseNotes,
    [property: JsonPropertyName("signature")] string Signature);

public enum UpdateAvailability { UpToDate = 0, Available = 1, Required = 2, Blocked = 3 }

public sealed record UpdateCheckResult(
    UpdateAvailability Availability,
    Version CurrentVersion,
    Version? AvailableVersion,
    UpdateManifest? Manifest,
    string Message);

public sealed record PreparedUpdate(
    UpdateManifest Manifest,
    string PackagePath,
    string StagingDirectory,
    DateTimeOffset PreparedAt);

public sealed record UpdateApplyPlan(
    int SchemaVersion,
    string TargetVersion,
    string InstallationDirectory,
    string DataRootDirectory,
    string StagingDirectory,
    string RollbackDirectory,
    int WaitForProcessId,
    string? RestartExecutable,
    string? PreUpdateBackupPath,
    DateTimeOffset CreatedAt);

public interface IPosUpdateService
{
    Task<UpdateCheckResult> CheckAsync(Version currentVersion, CancellationToken cancellationToken = default);
    Task<PreparedUpdate> DownloadAndStageAsync(UpdateManifest manifest, CancellationToken cancellationToken = default);
    Task<string> WriteApplyPlanAsync(PreparedUpdate prepared, string installationDirectory, string dataRootDirectory, int waitForProcessId, string? restartExecutable, string? preUpdateBackupPath, CancellationToken cancellationToken = default);
    Process LaunchApplyAgent(string planPath);
}
