namespace BusinessOS.POS.Persistence.Entities;

public sealed class PurchaseReturnEntity
{
    public long Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public long GoodsReceiptId { get; set; }
    public long SupplierId { get; set; }
    public long CreatedByUserId { get; set; }
    public DateTime BusinessDate { get; set; }
    public string Reason { get; set; } = string.Empty;
    public decimal ReturnTotal { get; set; }
    public DateTimeOffset PostedAt { get; set; }
    public List<PurchaseReturnItemEntity> Items { get; } = [];
}
