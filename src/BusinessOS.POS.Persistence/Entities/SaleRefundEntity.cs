namespace BusinessOS.POS.Persistence.Entities;

public sealed class SaleRefundEntity
{
    public long Id { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public long SaleReturnId { get; set; }
    public string PaymentMethodCode { get; set; } = string.Empty;
    public long RecordedByUserId { get; set; }
    public decimal Amount { get; set; }
    public string? Reference { get; set; }
    public DateTimeOffset RefundedAt { get; set; }
    public string? Notes { get; set; }
}
