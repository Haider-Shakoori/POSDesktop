namespace BusinessOS.POS.Persistence.Entities;

public sealed class CashierShiftEntity
{
    public long Id { get; set; }
    public long TerminalId { get; set; } = 1;
    public long UserId { get; set; }
    public DateTime BusinessDate { get; set; } = DateTime.Today;
    public string? OpenIdempotencyKey { get; set; }
    public string Status { get; set; } = "open";
    public decimal OpeningCash { get; set; }
    public decimal ExpectedCash { get; set; }
    public decimal? ActualCash { get; set; }
    public decimal? Variance { get; set; }
    public bool? VarianceWithinTolerance { get; set; }
    public string? VarianceReason { get; set; }
    public string? ClosingNotes { get; set; }
    public DateTimeOffset OpenedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public long? ClosedByUserId { get; set; }
    public DateTimeOffset? ReopenedAt { get; set; }
    public long? ReopenedByUserId { get; set; }
}
