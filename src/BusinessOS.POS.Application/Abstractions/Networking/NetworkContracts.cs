namespace BusinessOS.POS.Application.Abstractions.Networking;

public enum DeploymentMode { Standalone = 0, Server = 1, Client = 2 }

public sealed record NetworkConfiguration
{
    public const int DefaultServerPort = 5380;
    public DeploymentMode Mode { get; init; } = DeploymentMode.Standalone;
    public string ServerName { get; init; } = "Main POS Server";
    public int ServerPort { get; init; } = DefaultServerPort;
    public string? ServerHost { get; init; }
    public string? ServerId { get; init; }
    public string? ServerCertificateSha256 { get; init; }
    public string? TerminalId { get; init; }
    public string? TerminalName { get; init; }
    public bool IsConfigured { get; init; }

    public void Validate()
    {
        if (ServerPort is < 1024 or > 65535)
            throw new InvalidOperationException("The local POS server port must be between 1024 and 65535.");
        if (Mode == DeploymentMode.Client && IsConfigured)
        {
            if (string.IsNullOrWhiteSpace(ServerHost) || string.IsNullOrWhiteSpace(ServerId) ||
                string.IsNullOrWhiteSpace(ServerCertificateSha256) || string.IsNullOrWhiteSpace(TerminalId))
                throw new InvalidOperationException("A configured client terminal must contain its paired POS server identity.");
        }
    }
}

public interface INetworkConfigurationStore
{
    Task<NetworkConfiguration> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(NetworkConfiguration configuration, CancellationToken cancellationToken = default);
}

public sealed record TerminalPairingSecret(string TerminalId, string TerminalSecret, string ServerId, string ServerCertificateSha256);

public interface INetworkSecretStore
{
    Task<TerminalPairingSecret?> LoadTerminalPairingAsync(CancellationToken cancellationToken = default);
    Task SaveTerminalPairingAsync(TerminalPairingSecret secret, CancellationToken cancellationToken = default);
    Task ClearTerminalPairingAsync(CancellationToken cancellationToken = default);
    Task<string?> LoadServerCertificatePasswordAsync(CancellationToken cancellationToken = default);
    Task SaveServerCertificatePasswordAsync(string password, CancellationToken cancellationToken = default);
}

public sealed record LanTerminal(
    string TerminalId,
    string Name,
    string ComputerName,
    bool IsActive,
    DateTimeOffset RegisteredAt,
    DateTimeOffset? LastSeenAt);

public sealed record PairingCodeIssue(string Code, DateTimeOffset ExpiresAt);
public sealed record PairTerminalResult(string TerminalId, string TerminalSecret, string ServerId, DateTimeOffset RegisteredAt);
public sealed record LocalServerConnectionStatus(bool IsConnected, string? ServerId, string? ServerName, TimeSpan? Latency, DateTimeOffset? LastSeenAt, string Message);

public interface ILanTerminalRegistry
{
    Task<string> GetOrCreateServerIdAsync(CancellationToken cancellationToken = default);
    Task<PairingCodeIssue> CreatePairingCodeAsync(TimeSpan lifetime, CancellationToken cancellationToken = default);
    Task<PairTerminalResult> PairAsync(string pairingCode, string terminalId, string name, string computerName, CancellationToken cancellationToken = default);
    Task<bool> AuthenticateAsync(string terminalId, string terminalSecret, CancellationToken cancellationToken = default);
    Task TouchAsync(string terminalId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LanTerminal>> ListAsync(CancellationToken cancellationToken = default);
    Task RevokeAsync(string terminalId, CancellationToken cancellationToken = default);
}

public interface ILanClientService
{
    Task<NetworkConfiguration> PairAsync(string host, int port, string serverId, string certificateSha256, string pairingCode, string terminalName, CancellationToken cancellationToken = default);
    Task<LocalServerConnectionStatus> TestConnectionAsync(CancellationToken cancellationToken = default);
}
