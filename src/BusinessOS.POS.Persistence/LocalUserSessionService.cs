using BusinessOS.POS.Application.Abstractions.Authentication;
using BusinessOS.POS.Domain.Authentication;
using BusinessOS.POS.Persistence.Entities;
using BusinessOS.POS.Persistence.Security;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.POS.Persistence;

public sealed class LocalUserSessionService(
    IDbContextFactory<PosDbContext> contextFactory,
    PasswordHasher passwordHasher,
    LoginAttemptThrottle throttle)
    : IUserSessionService
{
    public UserSessionSnapshot? Current { get; private set; }

    public async Task<UserSessionSnapshot> LoginAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        var normalized = OwnerBootstrapService.NormalizeUsername(username);

        if (!throttle.CanAttempt(normalized, out var secondsRemaining))
        {
            throw new LoginThrottledException(secondsRemaining);
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var user = await context.Users
            .Include(x => x.Roles)
            .ThenInclude(x => x.Permissions)
            .SingleOrDefaultAsync(x => x.NormalizedUsername == normalized, cancellationToken);

        if (user is null || !user.IsActive || !passwordHasher.Verify(password, user.PasswordHash))
        {
            throttle.RegisterFailure(normalized);
            throw new InvalidCredentialsException();
        }

        throttle.Clear(normalized);

        Current = ToSnapshot(user);
        context.AuditLogs.Add(new AuditLogEntity
        {
            ActorUserId = user.Id,
            Event = "auth.login",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await context.SaveChangesAsync(cancellationToken);

        return Current;
    }

    public async Task UpdatePreferredLocaleAsync(
        string locale,
        CancellationToken cancellationToken = default)
    {
        if (locale is not ("en" or "fa" or "ps"))
        {
            throw new ArgumentException("Unsupported locale.", nameof(locale));
        }

        var current = Current ?? throw new InvalidOperationException("No user is signed in.");

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var user = await context.Users.SingleAsync(x => x.Id == current.UserId, cancellationToken);
        user.PreferredLocale = locale;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);

        Current = current with { PreferredLocale = locale };
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        var current = Current;
        if (current is not null)
        {
            await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
            context.AuditLogs.Add(new AuditLogEntity
            {
                ActorUserId = current.UserId,
                Event = "auth.logout",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await context.SaveChangesAsync(cancellationToken);
        }

        Current = null;
    }

    private static UserSessionSnapshot ToSnapshot(UserEntity user)
    {
        var roles = user.Roles
            .Select(x => x.Name)
            .ToHashSet(StringComparer.Ordinal);

        var permissions = user.Roles
            .SelectMany(x => x.Permissions)
            .Select(x => x.Name)
            .ToHashSet(StringComparer.Ordinal);

        return new UserSessionSnapshot(
            user.Id,
            user.Name,
            user.Username,
            user.PreferredLocale,
            roles,
            permissions);
    }
}
