namespace BusinessOS.POS.Persistence.Entities;

public sealed class ProductEntity
{
    public long Id { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string? NameFa { get; set; }
    public string? NamePs { get; set; }
    public long? CategoryId { get; set; }
    public CategoryEntity? Category { get; set; }
    public long? BrandId { get; set; }
    public BrandEntity? Brand { get; set; }
    public long BaseUnitId { get; set; }
    public UnitEntity BaseUnit { get; set; } = null!;
    public string? DescriptionEn { get; set; }
    public string? DescriptionFa { get; set; }
    public string? DescriptionPs { get; set; }
    public string? ShelfLocation { get; set; }
    public string? ImagePath { get; set; }
    public decimal PurchaseCost { get; set; }
    public decimal SellingPrice { get; set; }
    public decimal? MinimumSellingPrice { get; set; }
    public decimal? WholesalePrice { get; set; }
    public decimal StockOnHand { get; set; }
    public decimal MinimumStock { get; set; }
    public decimal ReorderQuantity { get; set; }
    public bool TrackStock { get; set; } = true;
    public bool TrackExpiry { get; set; }
    public bool IsActive { get; set; } = true;
    public List<ProductUnitEntity> Units { get; } = [];
    public List<ProductBarcodeEntity> Barcodes { get; } = [];
}
