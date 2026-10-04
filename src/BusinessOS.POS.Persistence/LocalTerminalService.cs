using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BusinessOS.POS.Application.Abstractions.Networking;
using BusinessOS.POS.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.POS.Persistence;

public sealed class LocalTerminalService(IDbContextFactory<PosDbContext> contextFactory)
    : ILocalTerminalService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<LocalServerIdentity> GetOrCreateServerIdentityAsync(
        string serverName,
        CancellationToken cancellationToken = default)
    {
        serverName = Required(serverName, "Server name");
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.LanServerIdentities.SingleOrDefaultAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;

        if (entity is null)
        {
            entity = new LanServerIdentityEntity
            {
                ServerId = Guid.CreateVersion7().ToString(),
                ServerName = serverName,
                CreatedAt = now,
                UpdatedAt = now,
            };
            context.LanServerIdentities.Add(entity);
        }
        else
        {
            entity.ServerName = serverName;
            entity.UpdatedAt = now;
        }

        await context.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    public async Task<PairingCodeIssue> CreatePairingCodeAsync(
        TimeSpan lifetime,
        CancellationToken cancellationToken = default)
    {
        if (lifetime < TimeSpan.FromSeconds(30) || lifetime > TimeSpan.FromMinutes(15))
            throw new InvalidOperationException("Pairing codes must live between 30 seconds and 15 minutes.");

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var salt = RandomNumberGenerator.GetBytes(16);
        var now = DateTimeOffset.UtcNow;
        var entity = new LanPairingCodeEntity
        {
            Id = Guid.CreateVersion7().ToString(),
            SaltBase64 = Convert.ToBase64String(salt),
            CodeHashBase64 = Convert.ToBase64String(HashPairingCode(code, salt)),
            CreatedAt = now,
            ExpiresAt = now.Add(lifetime),
            MaxAttempts = 5,
        };

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        context.LanPairingCodes.Add(entity);
        await context.SaveChangesAsync(cancellationToken);
        return new PairingCodeIssue(entity.Id, code, entity.ExpiresAt);
    }

    public async Task<PairTerminalResult> PairAsync(
        PairTerminalRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(request.TerminalId, out _))
            throw new InvalidOperationException("A valid terminal UUID is required.");

        var code = Required(request.PairingCode, "Pairing code");
        var name = Required(request.Name, "Terminal name");
        var computer = Required(request.ComputerName, "Computer name");
        var role = Required(request.TerminalRole, "Terminal role");

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;

        var candidates = await context.LanPairingCodes
            .Where(x => x.UsedAt == null && x.ExpiresAt >= now && x.FailedAttempts < x.MaxAttempts)
            .OrderByDescending(x => x.CreatedAt)
            .Take(25)
            .ToListAsync(cancellationToken);

        LanPairingCodeEntity? matched = null;
        foreach (var candidate in candidates)
        {
            var salt = Convert.FromBase64String(candidate.SaltBase64);
            var expected = Convert.FromBase64String(candidate.CodeHashBase64);
            var actual = HashPairingCode(code, salt);
            if (CryptographicOperations.FixedTimeEquals(expected, actual))
            {
                matched = candidate;
                break;
            }

            candidate.FailedAttempts++;
        }

        if (matched is null)
        {
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            throw new InvalidOperationException("The pairing code is invalid, expired, or already used.");
        }

        matched.UsedAt = now;
        var secret = Base64Url(RandomNumberGenerator.GetBytes(32));
        var hash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

        var terminal = await context.RegisteredLanTerminals
            .SingleOrDefaultAsync(x => x.Id == request.TerminalId, cancellationToken);
        if (terminal is null)
        {
            terminal = new RegisteredLanTerminalEntity
            {
                Id = request.TerminalId,
                RegisteredAt = now,
            };
            context.RegisteredLanTerminals.Add(terminal);
        }

        terminal.Name = name;
        terminal.ComputerName = computer;
        terminal.TerminalRole = role;

        TerminalEntity cashTerminal;
        if (terminal.CashTerminalId is not null)
        {
            cashTerminal = await context.Terminals.SingleOrDefaultAsync(
                x => x.Id == terminal.CashTerminalId.Value, cancellationToken)
                ?? throw new InvalidOperationException("The LAN terminal's cashier terminal mapping is missing.");
        }
        else
        {
            var codeSuffix = request.TerminalId.Replace("-", string.Empty)[..8].ToUpperInvariant();
            var cashCode = "LAN-" + codeSuffix;
            cashTerminal = await context.Terminals.SingleOrDefaultAsync(
                x => x.Code == cashCode, cancellationToken)
                ?? new TerminalEntity { Code = cashCode, Name = name, IsActive = true };

            if (cashTerminal.Id == 0)
                context.Terminals.Add(cashTerminal);

            await context.SaveChangesAsync(cancellationToken);
            terminal.CashTerminalId = cashTerminal.Id;
        }

        cashTerminal.Name = name;
        cashTerminal.IsActive = true;
        terminal.SecretHashBase64 = hash;
        terminal.IsActive = true;
        terminal.RevokedAt = null;
        terminal.LastSeenAt = now;

        var identity = await context.LanServerIdentities.SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("The Main POS Server identity has not been initialized.");

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new PairTerminalResult(terminal.Id, secret, identity.ServerId, terminal.RegisteredAt);
    }

    public async Task<RegisteredLanTerminal?> AuthenticateTerminalAsync(
        string terminalId,
        string terminalSecret,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(terminalId) || string.IsNullOrWhiteSpace(terminalSecret))
            return null;

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.RegisteredLanTerminals
            .SingleOrDefaultAsync(x => x.Id == terminalId, cancellationToken);
        if (entity is null || !entity.IsActive || entity.RevokedAt is not null)
            return null;

        byte[] expected;
        try { expected = Convert.FromBase64String(entity.SecretHashBase64); }
        catch (FormatException) { return null; }

        var actual = SHA256.HashData(Encoding.UTF8.GetBytes(terminalSecret));
        if (!CryptographicOperations.FixedTimeEquals(expected, actual))
            return null;

        entity.LastSeenAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    public async Task<RegisteredLanTerminal?> HeartbeatAsync(
        string terminalId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.RegisteredLanTerminals
            .SingleOrDefaultAsync(x => x.Id == terminalId && x.IsActive, cancellationToken);
        if (entity is null) return null;

        entity.LastSeenAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    public async Task<IReadOnlyList<RegisteredLanTerminal>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return (await context.RegisteredLanTerminals.AsNoTracking()
                .OrderBy(x => x.Name).ThenBy(x => x.Id)
                .ToListAsync(cancellationToken))
            .Select(Map).ToList();
    }

    public Task RevokeAsync(string terminalId, CancellationToken cancellationToken = default) =>
        SetActiveAsync(terminalId, false, cancellationToken);

    public Task ReactivateAsync(string terminalId, CancellationToken cancellationToken = default) =>
        SetActiveAsync(terminalId, true, cancellationToken);

    public async Task UpdateAllowedPermissionsAsync(
        string terminalId,
        IReadOnlySet<string> permissions,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.RegisteredLanTerminals.SingleOrDefaultAsync(
            x => x.Id == terminalId, cancellationToken)
            ?? throw new InvalidOperationException("LAN terminal was not found.");

        entity.AllowedPermissionsJson = JsonSerializer.Serialize(
            permissions.OrderBy(x => x, StringComparer.Ordinal).ToArray(), JsonOptions);
        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task SetActiveAsync(
        string terminalId,
        bool active,
        CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.RegisteredLanTerminals.SingleOrDefaultAsync(
            x => x.Id == terminalId, cancellationToken)
            ?? throw new InvalidOperationException("LAN terminal was not found.");

        entity.IsActive = active;
        entity.RevokedAt = active ? null : DateTimeOffset.UtcNow;

        if (entity.CashTerminalId is not null)
        {
            var cashTerminal = await context.Terminals.SingleOrDefaultAsync(
                x => x.Id == entity.CashTerminalId.Value, cancellationToken);
            if (cashTerminal is not null) cashTerminal.IsActive = active;
        }

        if (!active)
        {
            var now = DateTimeOffset.UtcNow;
            var activeSessions = await context.LanUserSessions
                .Where(x => x.TerminalId == terminalId && x.RevokedAt == null)
                .ToListAsync(cancellationToken);
            foreach (var session in activeSessions) session.RevokedAt = now;
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private static LocalServerIdentity Map(LanServerIdentityEntity x) =>
        new(x.ServerId, x.ServerName, x.CreatedAt, x.UpdatedAt);

    private static RegisteredLanTerminal Map(RegisteredLanTerminalEntity x)
    {
        var permissions = JsonSerializer.Deserialize<string[]>(
            x.AllowedPermissionsJson, JsonOptions) ?? [];
        return new RegisteredLanTerminal(
            x.Id, x.Name, x.ComputerName, x.TerminalRole, x.CashTerminalId, x.IsActive,
            x.RegisteredAt, x.LastSeenAt, x.RevokedAt,
            permissions.ToHashSet(StringComparer.Ordinal));
    }

    private static byte[] HashPairingCode(string code, byte[] salt)
    {
        var codeBytes = Encoding.UTF8.GetBytes(code);
        var material = new byte[salt.Length + codeBytes.Length];
        Buffer.BlockCopy(salt, 0, material, 0, salt.Length);
        Buffer.BlockCopy(codeBytes, 0, material, salt.Length, codeBytes.Length);
        try { return SHA256.HashData(material); }
        finally
        {
            CryptographicOperations.ZeroMemory(codeBytes);
            CryptographicOperations.ZeroMemory(material);
        }
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Required(string value, string label)
    {
        value = value.Trim();
        if (value.Length == 0) throw new InvalidOperationException(label + " is required.");
        if (value.Length > 160) throw new InvalidOperationException(label + " is too long.");
        return value;
    }
}
