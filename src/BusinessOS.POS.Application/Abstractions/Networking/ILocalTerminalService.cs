namespace BusinessOS.POS.Application.Abstractions.Networking;

public interface ILocalTerminalService
{
    Task<LocalServerIdentity> GetOrCreateServerIdentityAsync(
        string serverName, CancellationToken cancellationToken = default);
    Task<PairingCodeIssue> CreatePairingCodeAsync(
        TimeSpan lifetime, CancellationToken cancellationToken = default);
    Task<PairTerminalResult> PairAsync(
        PairTerminalRequest request, CancellationToken cancellationToken = default);
    Task<RegisteredTerminal?> AuthenticateTerminalAsync(
        string terminalId, string terminalSecret, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RegisteredTerminal>> ListAsync(CancellationToken cancellationToken = default);
    Task RenameAsync(string terminalId, string name, CancellationToken cancellationToken = default);
    Task RevokeAsync(string terminalId, CancellationToken cancellationToken = default);
    Task TouchAsync(string terminalId, CancellationToken cancellationToken = default);
}
