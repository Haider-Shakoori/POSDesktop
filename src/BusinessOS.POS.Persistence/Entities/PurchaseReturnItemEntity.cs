namespace BusinessOS.POS.Persistence.Entities;

public sealed class PurchaseReturnItemEntity
{
    public long Id { get; set; }
    public long PurchaseReturnId { get; set; }
    public long GoodsReceiptItemId { get; set; }
    public long InventoryCostLayerId { get; set; }
    public long StockMovementId { get; set; }
    public decimal Quantity { get; set; }
    public decimal QuantityBase { get; set; }
    public decimal ReturnAmount { get; set; }
    public decimal UnitCostBase { get; set; }
    public decimal CostAmount { get; set; }
}
