namespace BusinessOS.POS.Persistence.Entities;

public sealed class LanServerIdentityEntity
{
    public long Id { get; set; }
    public string ServerId { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
