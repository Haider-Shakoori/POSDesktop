namespace BusinessOS.POS.Persistence.Entities;

public sealed class LocalServerIdentityEntity
{
    public int Id { get; set; } = 1;
    public string ServerId { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
