namespace BusinessOS.POS.Persistence.Entities;

public sealed class PurchasePaymentEntity
{
    public long Id { get; set; }
    public long GoodsReceiptId { get; set; }
    public long SupplierId { get; set; }
    public long RecordedByUserId { get; set; }
    public DateTime BusinessDate { get; set; }
    public decimal Amount { get; set; }
    public string MethodCode { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public DateTimeOffset PaidAt { get; set; }
    public string? Notes { get; set; }
}
