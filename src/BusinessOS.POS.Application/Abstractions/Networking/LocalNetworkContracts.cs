namespace BusinessOS.POS.Application.Abstractions.Networking;

public sealed record LocalServerIdentity(
    string ServerId,
    string ServerName,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record RegisteredLanTerminal(
    string TerminalId,
    string Name,
    string ComputerName,
    string TerminalRole,
    bool IsActive,
    DateTimeOffset RegisteredAt,
    DateTimeOffset? LastSeenAt,
    DateTimeOffset? RevokedAt,
    IReadOnlySet<string> AllowedPermissions);

public sealed record PairingCodeIssue(
    string PairingId,
    string Code,
    DateTimeOffset ExpiresAt);

public sealed record PairTerminalRequest(
    string PairingCode,
    string TerminalId,
    string Name,
    string ComputerName,
    string TerminalRole);

public sealed record PairTerminalResult(
    string TerminalId,
    string TerminalSecret,
    string ServerId,
    DateTimeOffset RegisteredAt);

public sealed record TerminalPairingSecret(
    string TerminalId,
    string TerminalSecret,
    string ServerId,
    string ServerCertificateSha256);

public sealed record LocalServerDiscoveryAdvertisement(
    string Service,
    string ApiVersion,
    string ServerId,
    string ServerName,
    string HostName,
    int Port,
    string CertificateSha256);

public sealed record LocalServerHealth(
    bool Available,
    string ApiVersion,
    string ServerId,
    DateTimeOffset ServerTime);

public sealed record LocalServerConnectionStatus(
    bool IsConnected,
    string? ServerId,
    string? ServerName,
    TimeSpan? Latency,
    DateTimeOffset? LastSeenAt,
    string? Message);
