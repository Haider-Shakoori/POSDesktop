namespace BusinessOS.POS.Persistence.Entities;

public sealed class CustomerCollectionAllocationEntity
{
    public long Id { get; set; }
    public long CustomerCollectionId { get; set; }
    public long SaleId { get; set; }
    public decimal Amount { get; set; }
}
