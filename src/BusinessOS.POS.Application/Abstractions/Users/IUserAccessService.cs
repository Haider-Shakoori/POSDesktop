namespace BusinessOS.POS.Application.Abstractions.Users;

public interface IUserAccessService
{
    Task<UserAccessSnapshot> GetAsync(CancellationToken cancellationToken = default);
    Task<AccessUserRow> SaveUserAsync(UserSaveRequest request, CancellationToken cancellationToken = default);
    Task ResetPasswordAsync(long userId, string newPassword, CancellationToken cancellationToken = default);
    Task<AccessRoleRow> SaveRoleAsync(RoleSaveRequest request, CancellationToken cancellationToken = default);
}

public sealed record AccessUserRow(
    long Id,
    string Name,
    string Username,
    string? Email,
    string PreferredLocale,
    bool IsActive,
    IReadOnlyList<long> RoleIds,
    string Roles);

public sealed record AccessRoleRow(
    long Id,
    string Name,
    string Label,
    bool IsSystem,
    IReadOnlyList<long> PermissionIds,
    string Permissions);

public sealed record AccessPermissionRow(long Id, string Name, string Label);

public sealed record AccessAuditRow(
    long Id,
    string? Actor,
    string Event,
    string? DetailsJson,
    DateTimeOffset CreatedAt);

public sealed record UserAccessSnapshot(
    bool CanViewAudit,
    IReadOnlyList<AccessUserRow> Users,
    IReadOnlyList<AccessRoleRow> Roles,
    IReadOnlyList<AccessPermissionRow> Permissions,
    IReadOnlyList<AccessAuditRow> Audit);

public sealed record UserSaveRequest(
    long? Id,
    string Name,
    string Username,
    string? Email,
    string PreferredLocale,
    bool IsActive,
    IReadOnlyList<long> RoleIds,
    string? Password);

public sealed record RoleSaveRequest(
    long? Id,
    string Name,
    string Label,
    IReadOnlyList<long> PermissionIds);
