using System.Security.Cryptography;
using System.Text;
using BusinessOS.POS.Application.Abstractions.Networking;
using BusinessOS.POS.Domain.Authentication;
using BusinessOS.POS.Persistence;
using BusinessOS.POS.Persistence.Entities;
using BusinessOS.POS.Persistence.Security;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.POS.LocalServer.Authentication;

public sealed class LanAuthenticationService(
    IDbContextFactory<PosDbContext> contextFactory,
    PasswordHasher passwordHasher)
{
    public async Task<LocalLoginResult> LoginAsync(
        string terminalId, string username, string password,
        CancellationToken cancellationToken = default)
    {
        var normalized = username.Trim().ToLowerInvariant();
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var user = await context.Users
            .Include(x => x.Roles).ThenInclude(x => x.Permissions)
            .SingleOrDefaultAsync(x => x.NormalizedUsername == normalized, cancellationToken);

        if (user is null || !user.IsActive || !passwordHasher.Verify(password, user.PasswordHash))
            throw new UnauthorizedAccessException("Invalid username or password.");

        var now = DateTimeOffset.UtcNow;
        var expires = now.AddHours(12);
        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        var token = Base64UrlEncode(tokenBytes);
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        try
        {
            var prior = await context.LanSessions
                .Where(x => x.TerminalId == terminalId && x.UserId == user.Id && x.RevokedAt == null)
                .ToListAsync(cancellationToken);
            foreach (var row in prior) row.RevokedAt = now;

            context.LanSessions.Add(new LanSessionEntity
            {
                Id = Guid.CreateVersion7().ToString(),
                TerminalId = terminalId,
                UserId = user.Id,
                TokenHash = Convert.ToHexString(hashBytes),
                CreatedAt = now,
                ExpiresAt = expires,
                LastSeenAt = now,
            });
            await context.SaveChangesAsync(cancellationToken);
            return new LocalLoginResult(token, ToSnapshot(user), expires);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(tokenBytes);
            CryptographicOperations.ZeroMemory(hashBytes);
        }
    }

    public async Task<UserSessionSnapshot?> AuthenticateSessionAsync(
        string terminalId, string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        var hash = Convert.ToHexString(hashBytes);
        CryptographicOperations.ZeroMemory(hashBytes);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var session = await context.LanSessions.SingleOrDefaultAsync(
            x => x.TokenHash == hash && x.TerminalId == terminalId, cancellationToken);
        if (session is null || session.RevokedAt is not null || session.ExpiresAt <= now) return null;

        var user = await context.Users
            .Include(x => x.Roles).ThenInclude(x => x.Permissions)
            .SingleOrDefaultAsync(x => x.Id == session.UserId, cancellationToken);
        if (user is null || !user.IsActive) return null;

        session.LastSeenAt = now;
        await context.SaveChangesAsync(cancellationToken);
        return ToSnapshot(user);
    }

    public async Task LogoutAsync(
        string terminalId, string token, CancellationToken cancellationToken = default)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        var hash = Convert.ToHexString(hashBytes);
        CryptographicOperations.ZeroMemory(hashBytes);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await context.LanSessions.SingleOrDefaultAsync(
            x => x.TokenHash == hash && x.TerminalId == terminalId && x.RevokedAt == null, cancellationToken);
        if (row is null) return;
        row.RevokedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
    }

    private static UserSessionSnapshot ToSnapshot(UserEntity user) => new(
        user.Id,
        user.Name,
        user.Username,
        user.PreferredLocale,
        user.Roles.Select(x => x.Name).ToHashSet(StringComparer.Ordinal),
        user.Roles.SelectMany(x => x.Permissions).Select(x => x.Name).ToHashSet(StringComparer.Ordinal));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
