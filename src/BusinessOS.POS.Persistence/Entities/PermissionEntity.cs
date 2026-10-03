namespace BusinessOS.POS.Persistence.Entities;

public sealed class PermissionEntity
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public List<RoleEntity> Roles { get; } = [];
}
