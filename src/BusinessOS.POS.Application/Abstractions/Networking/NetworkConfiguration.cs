namespace BusinessOS.POS.Application.Abstractions.Networking;

public sealed record NetworkConfiguration
{
    public const int DefaultServerPort = 5380;
    public const int DefaultDiscoveryPort = 5381;

    public DeploymentMode Mode { get; init; } = DeploymentMode.Standalone;
    public string ServerName { get; init; } = "Main POS Server";
    public int ServerPort { get; init; } = DefaultServerPort;
    public int DiscoveryPort { get; init; } = DefaultDiscoveryPort;
    public bool DiscoveryEnabled { get; init; } = true;

    public string? ServerHost { get; init; }
    public string? ServerId { get; init; }
    public string? ServerCertificateSha256 { get; init; }
    public string? TerminalId { get; init; }
    public string? TerminalName { get; init; }
    public string? TerminalRole { get; init; }
    public bool IsConfigured { get; init; }

    public void Validate()
    {
        if (ServerPort is < 1024 or > 65535)
            throw new InvalidOperationException("The POS LAN server port must be between 1024 and 65535.");
        if (DiscoveryPort is < 1024 or > 65535)
            throw new InvalidOperationException("The POS discovery port must be between 1024 and 65535.");

        if (Mode == DeploymentMode.Client && IsConfigured)
        {
            if (string.IsNullOrWhiteSpace(ServerHost))
                throw new InvalidOperationException("A client terminal must have a POS server address.");
            if (string.IsNullOrWhiteSpace(ServerId))
                throw new InvalidOperationException("A client terminal must remember the paired server identity.");
            if (string.IsNullOrWhiteSpace(ServerCertificateSha256) ||
                ServerCertificateSha256.Trim().Length != 64)
                throw new InvalidOperationException("A client terminal must remember the paired server certificate fingerprint.");
            if (string.IsNullOrWhiteSpace(TerminalId))
                throw new InvalidOperationException("A configured client terminal must remember its terminal identity.");
        }
    }
}
