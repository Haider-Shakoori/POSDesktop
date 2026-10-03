namespace BusinessOS.POS.Persistence.Entities;

public sealed class RoleEntity
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool IsSystem { get; set; }
    public List<UserEntity> Users { get; } = [];
    public List<PermissionEntity> Permissions { get; } = [];
}
