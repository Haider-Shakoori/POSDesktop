namespace BusinessOS.POS.Persistence.Entities;

public sealed class HeldSaleItemEntity
{
    public long Id { get; set; }
    public long HeldSaleId { get; set; }
    public long ProductUnitId { get; set; }
    public string ProductNameSnapshot { get; set; } = string.Empty;
    public string SkuSnapshot { get; set; } = string.Empty;
    public string UnitNameSnapshot { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal LineDiscountAmount { get; set; }
    public decimal UnitPriceSnapshot { get; set; }
}
