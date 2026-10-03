namespace BusinessOS.POS.Persistence.Entities;

public sealed class ProductBarcodeEntity
{
    public long Id { get; set; }
    public long ProductId { get; set; }
    public ProductEntity Product { get; set; } = null!;
    public long ProductUnitId { get; set; }
    public ProductUnitEntity ProductUnit { get; set; } = null!;
    public string Barcode { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
}
