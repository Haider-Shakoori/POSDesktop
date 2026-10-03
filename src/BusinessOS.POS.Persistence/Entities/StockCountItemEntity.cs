namespace BusinessOS.POS.Persistence.Entities;

public sealed class StockCountItemEntity
{
    public long Id { get; set; }
    public long StockCountId { get; set; }
    public long ProductId { get; set; }
    public long? ProductBatchId { get; set; }
    public decimal ExpectedQuantityBase { get; set; }
    public decimal PhysicalQuantityBase { get; set; }
    public decimal VarianceQuantityBase { get; set; }
    public long? StockMovementId { get; set; }
    public decimal CostAmount { get; set; }
}
