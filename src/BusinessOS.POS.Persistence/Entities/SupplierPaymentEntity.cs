namespace BusinessOS.POS.Persistence.Entities;

public sealed class SupplierPaymentEntity
{
    public long Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public long SupplierId { get; set; }
    public long RecordedByUserId { get; set; }
    public DateTime BusinessDate { get; set; }
    public decimal Amount { get; set; }
    public string MethodCode { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public DateTimeOffset PaidAt { get; set; }
    public string? Notes { get; set; }
    public List<SupplierPaymentAllocationEntity> Allocations { get; } = [];
}
