namespace BusinessOS.POS.Persistence.Entities;

public sealed class SaleItemEntity
{
    public long Id { get; set; }
    public long SaleId { get; set; }
    public long ProductId { get; set; }
    public long ProductUnitId { get; set; }
    public string ProductNameSnapshot { get; set; } = string.Empty;
    public string SkuSnapshot { get; set; } = string.Empty;
    public string UnitNameSnapshot { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal ConversionFactor { get; set; }
    public decimal QuantityBase { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal? MinimumUnitPrice { get; set; }
    public decimal LineSubtotal { get; set; }
    public decimal LineDiscountAmount { get; set; }
    public decimal AllocatedSaleDiscount { get; set; }
    public decimal LineNetTotal { get; set; }
    public decimal CogsAmount { get; set; }
    public decimal GrossProfit { get; set; }
}
