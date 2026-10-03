namespace BusinessOS.POS.Persistence.Entities;

public sealed class SaleReturnEntity
{
    public long Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public long SaleId { get; set; }
    public long CreatedByUserId { get; set; }
    public string Type { get; set; } = "return";
    public string Status { get; set; } = "posted";
    public string Reason { get; set; } = string.Empty;
    public decimal ReturnTotal { get; set; }
    public decimal CogsReversed { get; set; }
    public decimal ReceivableReversed { get; set; }
    public decimal RefundTotal { get; set; }
    public DateTimeOffset PostedAt { get; set; }
    public List<SaleReturnItemEntity> Items { get; } = [];
    public List<SaleRefundEntity> Refunds { get; } = [];
}
