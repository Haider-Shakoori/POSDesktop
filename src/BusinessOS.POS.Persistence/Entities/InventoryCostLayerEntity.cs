namespace BusinessOS.POS.Persistence.Entities;

public sealed class InventoryCostLayerEntity
{
    public long Id { get; set; }
    public long ProductId { get; set; }
    public long? ProductBatchId { get; set; }
    public long? SourceStockMovementId { get; set; }
    public decimal InitialQuantityBase { get; set; }
    public decimal RemainingQuantityBase { get; set; }
    public decimal UnitCostBase { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
}
