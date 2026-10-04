namespace BusinessOS.POS.Application.Abstractions.Networking;

public interface ILocalTerminalService
{
    Task<LocalServerIdentity> GetOrCreateServerIdentityAsync(
        string serverName,
        CancellationToken cancellationToken = default);

    Task<PairingCodeIssue> CreatePairingCodeAsync(
        TimeSpan lifetime,
        CancellationToken cancellationToken = default);

    Task<PairTerminalResult> PairAsync(
        PairTerminalRequest request,
        CancellationToken cancellationToken = default);

    Task<RegisteredLanTerminal?> AuthenticateTerminalAsync(
        string terminalId,
        string terminalSecret,
        CancellationToken cancellationToken = default);

    Task<RegisteredLanTerminal?> HeartbeatAsync(
        string terminalId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RegisteredLanTerminal>> ListAsync(
        CancellationToken cancellationToken = default);

    Task RevokeAsync(string terminalId, CancellationToken cancellationToken = default);
    Task ReactivateAsync(string terminalId, CancellationToken cancellationToken = default);

    Task UpdateAllowedPermissionsAsync(
        string terminalId,
        IReadOnlySet<string> permissions,
        CancellationToken cancellationToken = default);
}
