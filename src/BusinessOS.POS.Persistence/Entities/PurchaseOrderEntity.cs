namespace BusinessOS.POS.Persistence.Entities;

public sealed class PurchaseOrderEntity
{
    public long Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public long SupplierId { get; set; }
    public long CreatedByUserId { get; set; }
    public long? ApprovedByUserId { get; set; }
    public string Status { get; set; } = "draft";
    public DateTime OrderDate { get; set; }
    public DateTime? ExpectedDate { get; set; }
    public string? SupplierReference { get; set; }
    public decimal Subtotal { get; set; }
    public decimal LineDiscountTotal { get; set; }
    public decimal OrderDiscountAmount { get; set; }
    public decimal NetTotal { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public string? Notes { get; set; }
    public List<PurchaseOrderItemEntity> Items { get; } = [];
}
