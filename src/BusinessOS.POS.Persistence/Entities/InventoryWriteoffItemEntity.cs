namespace BusinessOS.POS.Persistence.Entities;

public sealed class InventoryWriteoffItemEntity
{
    public long Id { get; set; }
    public long InventoryWriteoffId { get; set; }
    public long ProductId { get; set; }
    public long? ProductBatchId { get; set; }
    public long StockMovementId { get; set; }
    public decimal QuantityBase { get; set; }
    public decimal CostAmount { get; set; }
}
