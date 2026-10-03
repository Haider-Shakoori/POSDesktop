namespace BusinessOS.POS.Persistence.Entities;

public sealed class ProductEntity
{
    public long Id { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string? NameFa { get; set; }
    public string? NamePs { get; set; }
    public decimal PurchaseCost { get; set; }
    public decimal SellingPrice { get; set; }
    public decimal? MinimumSellingPrice { get; set; }
    public decimal StockOnHand { get; set; }
    public decimal MinimumStock { get; set; }
    public bool TrackStock { get; set; } = true;
    public bool TrackExpiry { get; set; }
    public bool IsActive { get; set; } = true;
    public List<ProductUnitEntity> Units { get; } = [];
    public List<ProductBarcodeEntity> Barcodes { get; } = [];
}
