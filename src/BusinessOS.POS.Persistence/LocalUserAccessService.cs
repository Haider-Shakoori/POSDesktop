using System.Text.Json;
using System.Text.RegularExpressions;
using BusinessOS.POS.Application.Abstractions.Authentication;
using BusinessOS.POS.Application.Abstractions.Users;
using BusinessOS.POS.Persistence.Entities;
using BusinessOS.POS.Persistence.Security;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.POS.Persistence;

public sealed partial class LocalUserAccessService(
    IDbContextFactory<PosDbContext> contextFactory,
    IUserSessionService sessions,
    IPermissionAuthorizer authorizer,
    PasswordHasher passwordHasher)
    : IUserAccessService
{
    public async Task<UserAccessSnapshot> GetAsync(CancellationToken cancellationToken = default)
    {
        authorizer.Demand("users.manage");
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var users = await context.Users.AsNoTracking()
            .Include(x => x.Roles)
            .OrderBy(x => x.Name)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        var roles = await context.Roles.AsNoTracking()
            .Include(x => x.Permissions)
            .OrderByDescending(x => x.IsSystem)
            .ThenBy(x => x.Label)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        var permissions = await context.Permissions.AsNoTracking()
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        var audits = await context.AuditLogs.AsNoTracking()
            .OrderByDescending(x => x.Id)
            .Take(200)
            .ToListAsync(cancellationToken);
        var actorIds = audits.Where(x => x.ActorUserId is not null)
            .Select(x => x.ActorUserId!.Value)
            .Distinct()
            .ToList();
        var actorNames = actorIds.Count == 0
            ? new Dictionary<long, string>()
            : await context.Users.AsNoTracking()
                .Where(x => actorIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        return new UserAccessSnapshot(
            users.Select(MapUser).ToList(),
            roles.Select(MapRole).ToList(),
            permissions.Select(x => new AccessPermissionRow(x.Id, x.Name, x.Label)).ToList(),
            audits.Select(x => new AccessAuditRow(
                x.Id,
                x.ActorUserId is null ? null : actorNames.GetValueOrDefault(x.ActorUserId.Value),
                x.Event,
                x.DetailsJson,
                x.CreatedAt)).ToList());
    }

    public async Task<AccessUserRow> SaveUserAsync(
        UserSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        authorizer.Demand("users.manage");
        var current = sessions.Current ?? throw new InvalidOperationException("No user is signed in.");

        var name = request.Name.Trim();
        var username = request.Username.Trim();
        var normalized = username.ToLowerInvariant();
        var email = Clean(request.Email);
        var locale = request.PreferredLocale.Trim().ToLowerInvariant();
        if (name.Length == 0) throw new InvalidOperationException("Name is required.");
        if (!UsernamePattern().IsMatch(username))
            throw new InvalidOperationException("Username must be 3-50 characters using letters, numbers, dot, underscore or dash.");
        if (locale is not ("en" or "fa" or "ps"))
            throw new InvalidOperationException("Preferred language must be English, Dari or Pashto.");
        if (request.RoleIds.Count == 0)
            throw new InvalidOperationException("At least one role is required.");

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        if (await context.Users.AnyAsync(
            x => x.NormalizedUsername == normalized && (request.Id == null || x.Id != request.Id.Value),
            cancellationToken))
            throw new InvalidOperationException("That username is already in use.");

        var roles = await context.Roles
            .Where(x => request.RoleIds.Contains(x.Id))
            .ToListAsync(cancellationToken);
        if (roles.Count != request.RoleIds.Distinct().Count())
            throw new InvalidOperationException("One or more selected roles do not exist.");

        UserEntity user;
        var isNew = request.Id is null;
        if (isNew)
        {
            if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
                throw new InvalidOperationException("A password of at least 8 characters is required for a new user.");

            var now = DateTimeOffset.UtcNow;
            user = new UserEntity
            {
                Name = name,
                Username = username,
                NormalizedUsername = normalized,
                Email = email,
                PasswordHash = passwordHasher.Hash(request.Password),
                PreferredLocale = locale,
                IsActive = request.IsActive,
                CreatedAt = now,
                UpdatedAt = now,
            };
            foreach (var role in roles) user.Roles.Add(role);
            context.Users.Add(user);
        }
        else
        {
            user = await context.Users
                .Include(x => x.Roles)
                .SingleOrDefaultAsync(x => x.Id == request.Id.Value, cancellationToken)
                ?? throw new InvalidOperationException("User was not found.");

            if (user.Id == current.UserId && !request.IsActive)
                throw new InvalidOperationException("You cannot deactivate your own signed-in account.");

            var hadOwner = user.Roles.Any(x => x.Name == "owner");
            var willHaveOwner = roles.Any(x => x.Name == "owner");
            if (hadOwner && (!request.IsActive || !willHaveOwner))
            {
                var otherActiveOwners = await context.Users
                    .Include(x => x.Roles)
                    .CountAsync(x => x.Id != user.Id && x.IsActive && x.Roles.Any(r => r.Name == "owner"),
                        cancellationToken);
                if (otherActiveOwners == 0)
                    throw new InvalidOperationException("The last active owner cannot be deactivated or lose the owner role.");
            }

            user.Name = name;
            user.Username = username;
            user.NormalizedUsername = normalized;
            user.Email = email;
            user.PreferredLocale = locale;
            user.IsActive = request.IsActive;
            user.UpdatedAt = DateTimeOffset.UtcNow;
            if (!string.IsNullOrWhiteSpace(request.Password))
            {
                if (request.Password.Length < 8)
                    throw new InvalidOperationException("Password must be at least 8 characters.");
                user.PasswordHash = passwordHasher.Hash(request.Password);
            }

            user.Roles.Clear();
            foreach (var role in roles) user.Roles.Add(role);
        }

        await context.SaveChangesAsync(cancellationToken);
        context.AuditLogs.Add(new AuditLogEntity
        {
            ActorUserId = current.UserId,
            Event = isNew ? "access.user.created" : "access.user.updated",
            CreatedAt = DateTimeOffset.UtcNow,
            DetailsJson = JsonSerializer.Serialize(new
            {
                user.Id,
                user.Username,
                user.IsActive,
                roles = roles.Select(x => x.Name).ToArray(),
            }),
        });
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return MapUser(user);
    }

    public async Task ResetPasswordAsync(
        long userId,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        authorizer.Demand("users.manage");
        var current = sessions.Current ?? throw new InvalidOperationException("No user is signed in.");
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
            throw new InvalidOperationException("Password must be at least 8 characters.");

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var user = await context.Users.SingleOrDefaultAsync(x => x.Id == userId, cancellationToken)
            ?? throw new InvalidOperationException("User was not found.");
        user.PasswordHash = passwordHasher.Hash(newPassword);
        user.UpdatedAt = DateTimeOffset.UtcNow;
        context.AuditLogs.Add(new AuditLogEntity
        {
            ActorUserId = current.UserId,
            Event = "access.user.password_reset",
            CreatedAt = DateTimeOffset.UtcNow,
            DetailsJson = JsonSerializer.Serialize(new { user.Id, user.Username }),
        });
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<AccessRoleRow> SaveRoleAsync(
        RoleSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        authorizer.Demand("users.manage");
        var current = sessions.Current ?? throw new InvalidOperationException("No user is signed in.");
        var name = request.Name.Trim().ToLowerInvariant();
        var label = request.Label.Trim();
        if (!RoleNamePattern().IsMatch(name))
            throw new InvalidOperationException("Role name must be 3-100 lowercase characters using letters, numbers, dot, underscore or dash.");
        if (label.Length == 0) throw new InvalidOperationException("Role label is required.");

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        if (await context.Roles.AnyAsync(
            x => x.Name == name && (request.Id == null || x.Id != request.Id.Value),
            cancellationToken))
            throw new InvalidOperationException("That role name is already in use.");

        var permissions = await context.Permissions
            .Where(x => request.PermissionIds.Contains(x.Id))
            .ToListAsync(cancellationToken);
        if (permissions.Count != request.PermissionIds.Distinct().Count())
            throw new InvalidOperationException("One or more selected permissions do not exist.");

        RoleEntity role;
        var isNew = request.Id is null;
        if (isNew)
        {
            role = new RoleEntity { Name = name, Label = label, IsSystem = false };
            foreach (var permission in permissions) role.Permissions.Add(permission);
            context.Roles.Add(role);
        }
        else
        {
            role = await context.Roles
                .Include(x => x.Permissions)
                .SingleOrDefaultAsync(x => x.Id == request.Id.Value, cancellationToken)
                ?? throw new InvalidOperationException("Role was not found.");
            if (role.IsSystem)
                throw new InvalidOperationException("System roles are read-only. Create a custom role for different permissions.");

            role.Name = name;
            role.Label = label;
            role.Permissions.Clear();
            foreach (var permission in permissions) role.Permissions.Add(permission);
        }

        await context.SaveChangesAsync(cancellationToken);
        context.AuditLogs.Add(new AuditLogEntity
        {
            ActorUserId = current.UserId,
            Event = isNew ? "access.role.created" : "access.role.updated",
            CreatedAt = DateTimeOffset.UtcNow,
            DetailsJson = JsonSerializer.Serialize(new
            {
                role.Id,
                role.Name,
                permissions = permissions.Select(x => x.Name).ToArray(),
            }),
        });
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return MapRole(role);
    }

    private static AccessUserRow MapUser(UserEntity x) =>
        new(x.Id, x.Name, x.Username, x.Email, x.PreferredLocale, x.IsActive,
            x.Roles.Select(r => r.Id).OrderBy(id => id).ToList(),
            string.Join(", ", x.Roles.OrderBy(r => r.Label).Select(r => r.Label)));

    private static AccessRoleRow MapRole(RoleEntity x) =>
        new(x.Id, x.Name, x.Label, x.IsSystem,
            x.Permissions.Select(p => p.Id).OrderBy(id => id).ToList(),
            string.Join(", ", x.Permissions.OrderBy(p => p.Label).Select(p => p.Label)));

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex("^[A-Za-z0-9._-]{3,50}$")]
    private static partial Regex UsernamePattern();

    [GeneratedRegex("^[a-z0-9._-]{3,100}$")]
    private static partial Regex RoleNamePattern();
}
