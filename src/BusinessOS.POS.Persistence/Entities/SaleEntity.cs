namespace BusinessOS.POS.Persistence.Entities;

public sealed class SaleEntity
{
    public long Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string RequestFingerprint { get; set; } = string.Empty;
    public long CashierUserId { get; set; }
    public long? CashierShiftId { get; set; }
    public string Status { get; set; } = "completed";
    public string PaymentStatus { get; set; } = "paid";
    public string CustomerNameSnapshot { get; set; } = "Walk-in Customer";
    public decimal Subtotal { get; set; }
    public decimal LineDiscountTotal { get; set; }
    public decimal SaleDiscountAmount { get; set; }
    public decimal NetTotal { get; set; }
    public decimal CogsTotal { get; set; }
    public decimal GrossProfit { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal ChangeAmount { get; set; }
    public DateTimeOffset SoldAt { get; set; }
    public string? Notes { get; set; }
    public List<SaleItemEntity> Items { get; } = [];
    public List<SalePaymentEntity> Payments { get; } = [];
}
