namespace BusinessOS.POS.Persistence.Entities;

public sealed class CustomerCollectionEntity
{
    public long Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public long CustomerId { get; set; }
    public string PaymentMethodCode { get; set; } = string.Empty;
    public long RecordedByUserId { get; set; }
    public DateTime BusinessDate { get; set; }
    public decimal Amount { get; set; }
    public decimal TenderedAmount { get; set; }
    public decimal ChangeAmount { get; set; }
    public string? Reference { get; set; }
    public DateTimeOffset CollectedAt { get; set; }
    public string? Notes { get; set; }
    public List<CustomerCollectionAllocationEntity> Allocations { get; } = [];
}
