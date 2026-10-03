namespace BusinessOS.POS.Persistence.Entities;

public sealed class CashierShiftClosureEntity
{
    public long Id { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public long CashierShiftId { get; set; }
    public int Version { get; set; }
    public long ClosedByUserId { get; set; }
    public decimal ExpectedCash { get; set; }
    public decimal ActualCash { get; set; }
    public decimal Variance { get; set; }
    public decimal Tolerance { get; set; }
    public bool WithinTolerance { get; set; }
    public string? VarianceReason { get; set; }
    public string? ClosingNotes { get; set; }
    public DateTimeOffset ClosedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
