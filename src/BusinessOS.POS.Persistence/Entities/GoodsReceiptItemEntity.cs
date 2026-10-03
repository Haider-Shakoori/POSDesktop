namespace BusinessOS.POS.Persistence.Entities;

public sealed class GoodsReceiptItemEntity
{
    public long Id { get; set; }
    public long GoodsReceiptId { get; set; }
    public long? PurchaseOrderItemId { get; set; }
    public long ProductId { get; set; }
    public long ProductUnitId { get; set; }
    public decimal Quantity { get; set; }
    public decimal ConversionFactor { get; set; }
    public decimal QuantityBase { get; set; }
    public decimal SourceUnitCost { get; set; }
    public decimal LineSubtotal { get; set; }
    public decimal LineDiscountAmount { get; set; }
    public decimal AllocatedReceiptDiscount { get; set; }
    public decimal AllocatedExpense { get; set; }
    public decimal LandedTotal { get; set; }
    public decimal SourceUnitLandedCost { get; set; }
    public decimal BaseUnitLandedCost { get; set; }
    public string? BatchNumber { get; set; }
    public DateTime? ManufacturedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public long? ProductBatchId { get; set; }
    public long? StockMovementId { get; set; }
    public long? InventoryCostLayerId { get; set; }
}
