namespace BusinessOS.POS.Domain.Authentication;

public sealed record UserSessionSnapshot(
    long UserId,
    string Name,
    string Username,
    string PreferredLocale,
    IReadOnlySet<string> Roles,
    IReadOnlySet<string> Permissions)
{
    public bool HasRole(string role) => Roles.Contains(role);

    public bool HasPermission(string permission) =>
        HasRole("owner") || Permissions.Contains(permission);
}
