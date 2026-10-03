namespace BusinessOS.POS.Persistence.Entities;

public sealed class GoodsReceiptEntity
{
    public long Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public long SupplierId { get; set; }
    public long? PurchaseOrderId { get; set; }
    public long CreatedByUserId { get; set; }
    public long PostedByUserId { get; set; }
    public string Status { get; set; } = "posted";
    public string? SupplierInvoiceReference { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public decimal Subtotal { get; set; }
    public decimal LineDiscountTotal { get; set; }
    public decimal ReceiptDiscountAmount { get; set; }
    public decimal ExpenseTotal { get; set; }
    public decimal NetTotal { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal BalanceDue { get; set; }
    public decimal ReturnedTotal { get; set; }
    public DateTimeOffset PostedAt { get; set; }
    public string? Notes { get; set; }
    public List<GoodsReceiptItemEntity> Items { get; } = [];
    public List<GoodsReceiptExpenseEntity> Expenses { get; } = [];
    public List<PurchasePaymentEntity> Payments { get; } = [];
}
