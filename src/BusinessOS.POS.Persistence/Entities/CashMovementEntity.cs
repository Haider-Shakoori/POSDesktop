namespace BusinessOS.POS.Persistence.Entities;

public sealed class CashMovementEntity
{
    public long Id { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public long CashierShiftId { get; set; }
    public long TerminalId { get; set; }
    public long? ActorUserId { get; set; }
    public string MovementType { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal ExpectedCashAfter { get; set; }
    public string? SourceType { get; set; }
    public long? SourceId { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
