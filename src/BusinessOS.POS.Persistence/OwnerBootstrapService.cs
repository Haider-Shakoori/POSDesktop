using System.Text.RegularExpressions;
using BusinessOS.POS.Application.Abstractions.Authentication;
using BusinessOS.POS.Persistence.Entities;
using BusinessOS.POS.Persistence.Security;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.POS.Persistence;

public sealed partial class OwnerBootstrapService(
    IDbContextFactory<PosDbContext> contextFactory,
    PasswordHasher passwordHasher)
    : IOwnerBootstrapService
{
    public async Task<bool> HasAnyUsersAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Users.AnyAsync(cancellationToken);
    }

    public async Task CreateOwnerAsync(
        string name,
        string username,
        string password,
        string preferredLocale,
        CancellationToken cancellationToken = default)
    {
        name = name.Trim();
        username = username.Trim();

        if (name.Length is < 2 or > 120)
        {
            throw new ArgumentException("Name must be between 2 and 120 characters.", nameof(name));
        }

        if (!UsernamePattern().IsMatch(username))
        {
            throw new ArgumentException(
                "Username must be 3-50 characters using letters, numbers, dot, dash or underscore.",
                nameof(username));
        }

        if (password.Length < 8)
        {
            throw new ArgumentException("Password must contain at least 8 characters.", nameof(password));
        }

        if (preferredLocale is not ("en" or "fa" or "ps"))
        {
            throw new ArgumentException("Unsupported language.", nameof(preferredLocale));
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        if (await context.Users.AnyAsync(cancellationToken))
        {
            throw new InvalidOperationException("Initial owner setup has already been completed.");
        }

        var ownerRole = await context.Roles.SingleAsync(x => x.Name == "owner", cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var user = new UserEntity
        {
            Name = name,
            Username = username,
            NormalizedUsername = NormalizeUsername(username),
            PasswordHash = passwordHasher.Hash(password),
            PreferredLocale = preferredLocale,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
        user.Roles.Add(ownerRole);

        context.Users.Add(user);
        await context.SaveChangesAsync(cancellationToken);

        context.AuditLogs.Add(new AuditLogEntity
        {
            ActorUserId = user.Id,
            Event = "auth.owner_bootstrapped",
            CreatedAt = now,
        });
        await context.SaveChangesAsync(cancellationToken);
    }

    internal static string NormalizeUsername(string username) =>
        username.Trim().ToLowerInvariant();

    [GeneratedRegex("^[A-Za-z0-9._-]{3,50}$")]
    private static partial Regex UsernamePattern();
}
