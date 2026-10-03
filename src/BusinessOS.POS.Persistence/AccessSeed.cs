using BusinessOS.POS.Domain.Access;
using BusinessOS.POS.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.POS.Persistence;

internal static class AccessSeed
{
    public static async Task ApplyAsync(PosDbContext context, CancellationToken cancellationToken)
    {
        var permissionNames = (await context.Permissions
                .Select(x => x.Name)
                .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var pair in PermissionCatalog.All)
        {
            if (!permissionNames.Contains(pair.Key))
            {
                context.Permissions.Add(new PermissionEntity
                {
                    Name = pair.Key,
                    Label = pair.Value,
                });
            }
        }

        var roleNames = (await context.Roles
                .Select(x => x.Name)
                .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var pair in RoleCatalog.SystemRoles)
        {
            if (!roleNames.Contains(pair.Key))
            {
                context.Roles.Add(new RoleEntity
                {
                    Name = pair.Key,
                    Label = pair.Value,
                    IsSystem = true,
                });
            }
        }

        await context.SaveChangesAsync(cancellationToken);

        var permissions = (await context.Permissions.ToListAsync(cancellationToken))
            .ToDictionary(x => x.Name, StringComparer.Ordinal);

        var roles = (await context.Roles
                .Include(x => x.Permissions)
                .Where(x => RoleCatalog.SystemRoles.Keys.Contains(x.Name))
                .ToListAsync(cancellationToken))
            .ToDictionary(x => x.Name, StringComparer.Ordinal);

        foreach (var pair in RoleCatalog.SystemRoles)
        {
            var roleName = pair.Key;
            var role = roles[roleName];
            role.Label = pair.Value;
            role.IsSystem = true;

            IEnumerable<string> expected;
            if (roleName is "owner" or "administrator")
            {
                expected = PermissionCatalog.All.Keys;
            }
            else if (RoleCatalog.Assignments.TryGetValue(roleName, out var assigned))
            {
                expected = assigned;
            }
            else
            {
                expected = Array.Empty<string>();
            }

            role.Permissions.Clear();
            foreach (var permissionName in expected)
            {
                role.Permissions.Add(permissions[permissionName]);
            }
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
