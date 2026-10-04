using BusinessOS.POS.Domain.Authentication;

namespace BusinessOS.POS.Application.Abstractions.Networking;

public interface ILanSessionStore
{
    Task<LanSessionIssue> CreateAsync(
        string terminalId,
        long userId,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default);

    Task<LanSessionPrincipal?> AuthenticateAsync(
        string terminalId,
        string token,
        CancellationToken cancellationToken = default);

    Task RevokeAsync(string terminalId, string token, CancellationToken cancellationToken = default);
    Task RevokeForTerminalAsync(string terminalId, CancellationToken cancellationToken = default);
}

public sealed record LanSessionIssue(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    UserSessionSnapshot User);

public sealed record LanSessionPrincipal(
    string TerminalId,
    UserSessionSnapshot User,
    IReadOnlySet<string> TerminalPermissions,
    DateTimeOffset ExpiresAt);
