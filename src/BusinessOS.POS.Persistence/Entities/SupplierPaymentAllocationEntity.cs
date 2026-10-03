namespace BusinessOS.POS.Persistence.Entities;

public sealed class SupplierPaymentAllocationEntity
{
    public long Id { get; set; }
    public long SupplierPaymentId { get; set; }
    public long GoodsReceiptId { get; set; }
    public decimal Amount { get; set; }
}
