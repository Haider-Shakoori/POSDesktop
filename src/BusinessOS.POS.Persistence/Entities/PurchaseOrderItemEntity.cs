namespace BusinessOS.POS.Persistence.Entities;

public sealed class PurchaseOrderItemEntity
{
    public long Id { get; set; }
    public long PurchaseOrderId { get; set; }
    public long ProductId { get; set; }
    public long ProductUnitId { get; set; }
    public decimal OrderedQuantity { get; set; }
    public decimal ReceivedQuantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal LineSubtotal { get; set; }
    public decimal LineDiscountAmount { get; set; }
    public decimal LineNetTotal { get; set; }
    public string? Notes { get; set; }
}
