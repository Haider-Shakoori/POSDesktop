namespace BusinessOS.POS.Persistence.Entities;

public sealed class SalePaymentEntity
{
    public long Id { get; set; }
    public long SaleId { get; set; }
    public string MethodCode { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? Reference { get; set; }
    public DateTimeOffset PaidAt { get; set; }
}
