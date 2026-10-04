using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BusinessOS.POS.Application.Abstractions.Networking;
using BusinessOS.POS.Domain.Authentication;
using BusinessOS.POS.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.POS.Persistence;

public sealed class LanSessionStore(IDbContextFactory<PosDbContext> contextFactory)
    : ILanSessionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<LanSessionIssue> CreateAsync(
        string terminalId,
        long userId,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default)
    {
        if (lifetime < TimeSpan.FromMinutes(5) || lifetime > TimeSpan.FromHours(24))
            throw new InvalidOperationException("LAN user sessions must live between 5 minutes and 24 hours.");

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var terminal = await context.RegisteredLanTerminals.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == terminalId && x.IsActive && x.RevokedAt == null, cancellationToken)
            ?? throw new UnauthorizedAccessException("The LAN terminal is not active.");

        var user = await context.Users
            .Include(x => x.Roles)
            .ThenInclude(x => x.Permissions)
            .SingleOrDefaultAsync(x => x.Id == userId && x.IsActive, cancellationToken)
            ?? throw new UnauthorizedAccessException("The POS user is not active.");

        var now = DateTimeOffset.UtcNow;
        var token = Base64Url(RandomNumberGenerator.GetBytes(32));
        var entity = new LanUserSessionEntity
        {
            TokenHashBase64 = Hash(token),
            TerminalId = terminalId,
            UserId = userId,
            CreatedAt = now,
            ExpiresAt = now.Add(lifetime),
        };
        context.LanUserSessions.Add(entity);
        await context.SaveChangesAsync(cancellationToken);

        return new LanSessionIssue(token, entity.ExpiresAt, ToSnapshot(user));
    }

    public async Task<LanSessionPrincipal?> AuthenticateAsync(
        string terminalId,
        string token,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(terminalId) || string.IsNullOrWhiteSpace(token))
            return null;

        var hash = Hash(token);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var session = await context.LanUserSessions.AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.TerminalId == terminalId &&
                     x.TokenHashBase64 == hash &&
                     x.RevokedAt == null,
                cancellationToken);
        if (session is null || session.ExpiresAt <= DateTimeOffset.UtcNow)
            return null;

        var terminal = await context.RegisteredLanTerminals.AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Id == terminalId && x.IsActive && x.RevokedAt == null,
                cancellationToken);
        if (terminal is null) return null;

        var user = await context.Users
            .Include(x => x.Roles)
            .ThenInclude(x => x.Permissions)
            .SingleOrDefaultAsync(x => x.Id == session.UserId && x.IsActive, cancellationToken);
        if (user is null) return null;

        var terminalPermissions = JsonSerializer.Deserialize<string[]>(
            terminal.AllowedPermissionsJson, JsonOptions) ?? [];

        return new LanSessionPrincipal(
            terminalId,
            ToSnapshot(user),
            terminalPermissions.ToHashSet(StringComparer.Ordinal),
            session.ExpiresAt);
    }

    public async Task RevokeAsync(
        string terminalId,
        string token,
        CancellationToken cancellationToken = default)
    {
        var hash = Hash(token);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var session = await context.LanUserSessions.SingleOrDefaultAsync(
            x => x.TerminalId == terminalId && x.TokenHashBase64 == hash && x.RevokedAt == null,
            cancellationToken);
        if (session is null) return;
        session.RevokedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task RevokeForTerminalAsync(
        string terminalId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var active = await context.LanUserSessions
            .Where(x => x.TerminalId == terminalId && x.RevokedAt == null)
            .ToListAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        foreach (var session in active) session.RevokedAt = now;
        await context.SaveChangesAsync(cancellationToken);
    }

    private static UserSessionSnapshot ToSnapshot(UserEntity user) =>
        new(
            user.Id,
            user.Name,
            user.Username,
            user.PreferredLocale,
            user.Roles.Select(x => x.Name).ToHashSet(StringComparer.Ordinal),
            user.Roles.SelectMany(x => x.Permissions).Select(x => x.Name)
                .ToHashSet(StringComparer.Ordinal));

    private static string Hash(string token) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
