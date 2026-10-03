namespace BusinessOS.POS.Persistence.Entities;

public sealed class BusinessDayEntity
{
    public long Id { get; set; }
    public DateTime BusinessDate { get; set; }
    public string Status { get; set; } = "open";
    public DateTimeOffset? ClosedAt { get; set; }
    public long? ClosedByUserId { get; set; }
    public DateTimeOffset? ReopenedAt { get; set; }
    public long? ReopenedByUserId { get; set; }
    public string? ReopenReason { get; set; }
}
