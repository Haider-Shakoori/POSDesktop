using BusinessOS.POS.Application.Abstractions.Authentication;
using BusinessOS.POS.Domain.Authentication;
using BusinessOS.POS.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.POS.LocalServer.Authentication;

public sealed class RequestUserSessionService(
    IHttpContextAccessor httpContextAccessor,
    IDbContextFactory<PosDbContext> contextFactory) : IUserSessionService
{
    public const string UserItemKey = "BusinessOS.POS.LanUser";

    public UserSessionSnapshot? Current =>
        httpContextAccessor.HttpContext?.Items[UserItemKey] as UserSessionSnapshot;

    public Task<UserSessionSnapshot> LoginAsync(
        string username, string password, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("LAN login is handled by the local server authentication endpoint.");

    public async Task UpdatePreferredLocaleAsync(string locale, CancellationToken cancellationToken = default)
    {
        if (locale is not ("en" or "fa" or "ps"))
            throw new ArgumentException("Unsupported locale.", nameof(locale));
        var current = Current ?? throw new InvalidOperationException("No LAN user is authenticated.");
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var user = await context.Users.SingleAsync(x => x.Id == current.UserId, cancellationToken);
        user.PreferredLocale = locale;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);

        var updated = current with { PreferredLocale = locale };
        if (httpContextAccessor.HttpContext is { } http) http.Items[UserItemKey] = updated;
    }

    public Task LogoutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
