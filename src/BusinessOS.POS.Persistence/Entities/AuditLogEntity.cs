namespace BusinessOS.POS.Persistence.Entities;

public sealed class AuditLogEntity
{
    public long Id { get; set; }
    public long? ActorUserId { get; set; }
    public string Event { get; set; } = string.Empty;
    public string? DetailsJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
