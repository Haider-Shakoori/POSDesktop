namespace BusinessOS.POS.Persistence.Entities;

public sealed class ProductUnitEntity
{
    public long Id { get; set; }
    public long ProductId { get; set; }
    public ProductEntity Product { get; set; } = null!;
    public long UnitId { get; set; }
    public UnitEntity Unit { get; set; } = null!;
    public decimal ConversionFactor { get; set; } = 1m;
    public bool CanPurchase { get; set; }
    public bool CanSell { get; set; } = true;
    public decimal? SellingPrice { get; set; }
    public decimal? MinimumSellingPrice { get; set; }
    public List<ProductBarcodeEntity> Barcodes { get; } = [];
}
