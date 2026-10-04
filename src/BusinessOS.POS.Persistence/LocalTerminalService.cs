using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BusinessOS.POS.Application.Abstractions.Networking;
using BusinessOS.POS.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.POS.Persistence;

public sealed class LocalTerminalService(IDbContextFactory<PosDbContext> contextFactory)
    : ILocalTerminalService
{
    private const int PairingDigits = 6;
    private const int PairingSaltBytes = 16;
    private const int TerminalSecretBytes = 32;

    public async Task<LocalServerIdentity> GetOrCreateServerIdentityAsync(
        string serverName, CancellationToken cancellationToken = default)
    {
        serverName = Normalize(serverName, 160, nameof(serverName));
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var tx = await context.Database.BeginTransactionAsync(cancellationToken);

        var row = await context.LocalServerIdentities.SingleOrDefaultAsync(x => x.Id == 1, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        if (row is null)
        {
            row = new LocalServerIdentityEntity
            {
                Id = 1,
                ServerId = Guid.CreateVersion7().ToString(),
                ServerName = serverName,
                CreatedAt = now,
                UpdatedAt = now,
            };
            context.LocalServerIdentities.Add(row);
            Audit(context, "network.server_identity.created", null, row.ServerId, now);
        }
        else if (!string.Equals(row.ServerName, serverName, StringComparison.Ordinal))
        {
            row.ServerName = serverName;
            row.UpdatedAt = now;
            Audit(context, "network.server_identity.renamed", null, serverName, now);
        }

        await context.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return ToModel(row);
    }

    public async Task<PairingCodeIssue> CreatePairingCodeAsync(
        TimeSpan lifetime, CancellationToken cancellationToken = default)
    {
        if (lifetime < TimeSpan.FromMinutes(1) || lifetime > TimeSpan.FromMinutes(30))
            throw new ArgumentOutOfRangeException(nameof(lifetime), "Pairing code lifetime must be 1-30 minutes.");

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (!await context.LocalServerIdentities.AnyAsync(x => x.Id == 1, cancellationToken))
            throw new InvalidOperationException("Initialize the Main POS Server identity before pairing terminals.");

        var now = DateTimeOffset.UtcNow;
        var id = Guid.CreateVersion7().ToString();
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
        var salt = RandomNumberGenerator.GetBytes(PairingSaltBytes);
        var hash = HashPairingCode(id, code, salt);
        try
        {
            context.TerminalPairingCodes.Add(new TerminalPairingCodeEntity
            {
                Id = id,
                SaltBase64 = Convert.ToBase64String(salt),
                CodeHashBase64 = Convert.ToBase64String(hash),
                CreatedAt = now,
                ExpiresAt = now.Add(lifetime),
                MaxAttempts = 5,
            });
            Audit(context, "network.pairing_code.created", null, id, now);
            await context.SaveChangesAsync(cancellationToken);
            return new PairingCodeIssue(id, code, now.Add(lifetime));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(salt);
            CryptographicOperations.ZeroMemory(hash);
        }
    }

    public async Task<PairTerminalResult> PairAsync(
        PairTerminalRequest request, CancellationToken cancellationToken = default)
    {
        var terminalId = NormalizeTerminalId(request.TerminalId);
        var name = Normalize(request.Name, 160, nameof(request.Name));
        var computer = Normalize(request.ComputerName, 160, nameof(request.ComputerName));
        var role = Normalize(request.TerminalRole, 80, nameof(request.TerminalRole));
        var code = NormalizePairingCode(request.PairingCode);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var tx = await context.Database.BeginTransactionAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;

        var candidates = (await context.TerminalPairingCodes
                .Where(x => x.UsedAt == null && x.FailedAttempts < x.MaxAttempts)
                .OrderByDescending(x => x.CreatedAt)
                .Take(25)
                .ToListAsync(cancellationToken))
            .Where(x => x.ExpiresAt >= now)
            .ToList();

        TerminalPairingCodeEntity? matched = null;
        foreach (var candidate in candidates)
        {
            var salt = Convert.FromBase64String(candidate.SaltBase64);
            var expected = Convert.FromBase64String(candidate.CodeHashBase64);
            var actual = HashPairingCode(candidate.Id, code, salt);
            try
            {
                if (CryptographicOperations.FixedTimeEquals(actual, expected))
                {
                    matched = candidate;
                    break;
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(salt);
                CryptographicOperations.ZeroMemory(expected);
                CryptographicOperations.ZeroMemory(actual);
            }
        }

        if (matched is null)
        {
            var newest = candidates.FirstOrDefault();
            if (newest is not null) newest.FailedAttempts++;
            Audit(context, "network.terminal_pairing.rejected", terminalId, "Invalid or expired code.", now);
            await context.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            throw new InvalidOperationException("The pairing code is invalid, expired, already used or locked.");
        }

        matched.UsedAt = now;

        var existing = await context.RegisteredTerminals.SingleOrDefaultAsync(x => x.Id == terminalId, cancellationToken);
        if (existing is not null && existing.IsActive && existing.RevokedAt is null)
            throw new InvalidOperationException("This terminal is already registered and active.");

        var secretBytes = RandomNumberGenerator.GetBytes(TerminalSecretBytes);
        var secret = Base64UrlEncode(secretBytes);
        var secretHash = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        try
        {
            existing ??= new RegisteredTerminalEntity { Id = terminalId, RegisteredAt = now };
            existing.Name = name;
            existing.ComputerName = computer;
            existing.TerminalRole = role;
            existing.SecretHash = Convert.ToHexString(secretHash);
            existing.IsActive = true;
            existing.RevokedAt = null;
            existing.LastSeenAt = now;
            if (context.Entry(existing).State == EntityState.Detached) context.RegisteredTerminals.Add(existing);

            var identity = await context.LocalServerIdentities.SingleAsync(x => x.Id == 1, cancellationToken);
            Audit(context, "network.terminal_pairing.success", terminalId, computer + " · " + role, now);
            await context.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return new PairTerminalResult(terminalId, secret, identity.ServerId, existing.RegisteredAt);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secretBytes);
            CryptographicOperations.ZeroMemory(secretHash);
        }
    }

    public async Task<RegisteredTerminal?> AuthenticateTerminalAsync(
        string terminalId, string terminalSecret, CancellationToken cancellationToken = default)
    {
        terminalId = NormalizeTerminalId(terminalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(terminalSecret);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await context.RegisteredTerminals.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == terminalId, cancellationToken);
        if (row is null || !row.IsActive || row.RevokedAt is not null) return null;

        var actual = SHA256.HashData(Encoding.UTF8.GetBytes(terminalSecret));
        byte[] expected;
        try { expected = Convert.FromHexString(row.SecretHash); }
        catch (FormatException) { CryptographicOperations.ZeroMemory(actual); return null; }

        try { return CryptographicOperations.FixedTimeEquals(actual, expected) ? ToModel(row) : null; }
        finally
        {
            CryptographicOperations.ZeroMemory(actual);
            CryptographicOperations.ZeroMemory(expected);
        }
    }

    public async Task<IReadOnlyList<RegisteredTerminal>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return (await context.RegisteredTerminals.AsNoTracking()
            .OrderByDescending(x => x.IsActive).ThenBy(x => x.Name)
            .ToListAsync(cancellationToken)).Select(ToModel).ToList();
    }

    public async Task RenameAsync(string terminalId, string name, CancellationToken cancellationToken = default)
    {
        terminalId = NormalizeTerminalId(terminalId);
        name = Normalize(name, 160, nameof(name));
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await context.RegisteredTerminals.SingleOrDefaultAsync(x => x.Id == terminalId, cancellationToken)
                  ?? throw new InvalidOperationException("Terminal was not found.");
        row.Name = name;
        Audit(context, "network.terminal.renamed", terminalId, name, DateTimeOffset.UtcNow);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task RevokeAsync(string terminalId, CancellationToken cancellationToken = default)
    {
        terminalId = NormalizeTerminalId(terminalId);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await context.RegisteredTerminals.SingleOrDefaultAsync(x => x.Id == terminalId, cancellationToken)
                  ?? throw new InvalidOperationException("Terminal was not found.");
        row.IsActive = false;
        row.RevokedAt = DateTimeOffset.UtcNow;
        Audit(context, "network.terminal.revoked", terminalId, row.Name, row.RevokedAt.Value);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task TouchAsync(string terminalId, CancellationToken cancellationToken = default)
    {
        terminalId = NormalizeTerminalId(terminalId);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await context.RegisteredTerminals.SingleOrDefaultAsync(
            x => x.Id == terminalId && x.IsActive && x.RevokedAt == null, cancellationToken)
                  ?? throw new InvalidOperationException("Terminal is not active.");
        row.LastSeenAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
    }

    private static void Audit(PosDbContext context, string evt, string? terminalId, string? message, DateTimeOffset at) =>
        context.AuditLogs.Add(new AuditLogEntity
        {
            Event = evt,
            CreatedAt = at,
            DetailsJson = "{\"terminal_id\":\"" + Escape(terminalId) + "\",\"message\":\"" + Escape(message) + "\"}",
        });

    private static LocalServerIdentity ToModel(LocalServerIdentityEntity x) =>
        new(x.ServerId, x.ServerName, x.CreatedAt, x.UpdatedAt);

    private static RegisteredTerminal ToModel(RegisteredTerminalEntity x) =>
        new(x.Id, x.Name, x.ComputerName, x.TerminalRole, x.IsActive, x.RegisteredAt, x.LastSeenAt, x.RevokedAt);

    private static string Normalize(string value, int max, string parameter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameter);
        value = value.Trim();
        if (value.Length > max) throw new ArgumentOutOfRangeException(parameter);
        return value;
    }

    private static string NormalizeTerminalId(string value)
    {
        value = Normalize(value, 80, nameof(value));
        if (!Guid.TryParse(value, out _)) throw new InvalidOperationException("Terminal ID must be a valid UUID.");
        return value.ToLowerInvariant();
    }

    private static string NormalizePairingCode(string value)
    {
        value = Normalize(value, PairingDigits, nameof(value));
        if (value.Length != PairingDigits || value.Any(ch => !char.IsAsciiDigit(ch)))
            throw new InvalidOperationException("Pairing code must contain exactly six digits.");
        return value;
    }

    private static byte[] HashPairingCode(string pairingId, string code, byte[] salt) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(pairingId + "|" + code + "|" + Convert.ToBase64String(salt)));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Escape(string? value) =>
        (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
}
