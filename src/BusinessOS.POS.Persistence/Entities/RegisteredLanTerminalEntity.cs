namespace BusinessOS.POS.Persistence.Entities;

public sealed class RegisteredLanTerminalEntity
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ComputerName { get; set; } = string.Empty;
    public string TerminalRole { get; set; } = string.Empty;
    public string SecretHashBase64 { get; set; } = string.Empty;
    public string AllowedPermissionsJson { get; set; } = "[]";
    public bool IsActive { get; set; } = true;
    public DateTimeOffset RegisteredAt { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}
