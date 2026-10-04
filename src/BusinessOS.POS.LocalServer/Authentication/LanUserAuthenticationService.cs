using BusinessOS.POS.Application.Abstractions.Networking;
using BusinessOS.POS.Persistence;
using BusinessOS.POS.Persistence.Security;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.POS.LocalServer.Authentication;

public sealed class LanUserAuthenticationService(
    IDbContextFactory<PosDbContext> contextFactory,
    PasswordHasher passwordHasher,
    LoginAttemptThrottle throttle,
    ILanSessionStore sessions)
{
    public async Task<LanSessionIssue> LoginAsync(
        string terminalId,
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        var normalized = username.Trim().ToLowerInvariant();
        if (!throttle.CanAttempt(terminalId + ":" + normalized, out var seconds))
            throw new InvalidOperationException(
                "Too many failed login attempts. Try again in " + seconds + " seconds.");

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var user = await context.Users.AsNoTracking()
            .SingleOrDefaultAsync(x => x.NormalizedUsername == normalized, cancellationToken);

        if (user is null || !user.IsActive || !passwordHasher.Verify(password, user.PasswordHash))
        {
            throttle.RegisterFailure(terminalId + ":" + normalized);
            throw new InvalidOperationException("Invalid username or password.");
        }

        throttle.Clear(terminalId + ":" + normalized);
        return await sessions.CreateAsync(
            terminalId,
            user.Id,
            TimeSpan.FromHours(8),
            cancellationToken);
    }
}
