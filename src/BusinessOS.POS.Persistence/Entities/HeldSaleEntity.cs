namespace BusinessOS.POS.Persistence.Entities;

public sealed class HeldSaleEntity
{
    public long Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public long CashierUserId { get; set; }
    public long? CustomerId { get; set; }
    public string CustomerNameSnapshot { get; set; } = "Walk-in Customer";
    public decimal SaleDiscountAmount { get; set; }
    public string Status { get; set; } = "held";
    public string? Notes { get; set; }
    public DateTimeOffset HeldAt { get; set; }
    public DateTimeOffset? ResumedAt { get; set; }
    public DateTimeOffset? ReleasedAt { get; set; }
    public List<HeldSaleItemEntity> Items { get; } = [];
}
