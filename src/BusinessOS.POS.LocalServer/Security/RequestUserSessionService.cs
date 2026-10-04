using BusinessOS.POS.Application.Abstractions.Authentication;
using BusinessOS.POS.Application.Abstractions.Networking;
using BusinessOS.POS.Domain.Authentication;
using BusinessOS.POS.LocalServer.Runtime;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.POS.LocalServer.Security;

public sealed class RequestUserSessionService(
    LanRequestContext requestContext,
    ILanSessionStore sessions,
    IDbContextFactory<BusinessOS.POS.Persistence.PosDbContext> contextFactory)
    : IUserSessionService
{
    public UserSessionSnapshot? Current => requestContext.Principal?.User;

    public Task<UserSessionSnapshot> LoginAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("LAN login is handled by the Local Server authentication endpoint.");

    public async Task UpdatePreferredLocaleAsync(
        string locale,
        CancellationToken cancellationToken = default)
    {
        if (locale is not ("en" or "fa" or "ps"))
            throw new ArgumentException("Unsupported locale.", nameof(locale));

        var principal = requestContext.Principal
            ?? throw new InvalidOperationException("No LAN POS user is signed in.");

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var user = await context.Users.SingleAsync(x => x.Id == principal.User.UserId, cancellationToken);
        user.PreferredLocale = locale;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);

        requestContext.Principal = principal with
        {
            User = principal.User with { PreferredLocale = locale }
        };
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        if (requestContext.Terminal is null ||
            string.IsNullOrWhiteSpace(requestContext.AccessToken))
            return;

        await sessions.RevokeAsync(
            requestContext.Terminal.TerminalId,
            requestContext.AccessToken,
            cancellationToken);

        requestContext.Principal = null;
        requestContext.AccessToken = null;
    }
}
