namespace BusinessOS.POS.Persistence.Entities;

public sealed class CashierShiftEntity
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string Status { get; set; } = "open";
    public decimal OpeningCash { get; set; }
    public DateTimeOffset OpenedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
}
