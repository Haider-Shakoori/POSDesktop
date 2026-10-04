namespace BusinessOS.POS.Application.Abstractions.Networking;

public interface INetworkSecretStore
{
    Task<TerminalPairingSecret?> LoadTerminalPairingAsync(CancellationToken cancellationToken = default);
    Task SaveTerminalPairingAsync(TerminalPairingSecret secret, CancellationToken cancellationToken = default);
    Task ClearTerminalPairingAsync(CancellationToken cancellationToken = default);
    Task<string?> LoadServerCertificatePasswordAsync(CancellationToken cancellationToken = default);
    Task SaveServerCertificatePasswordAsync(string password, CancellationToken cancellationToken = default);
}
