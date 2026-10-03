namespace BusinessOS.POS.Persistence.Entities;

public sealed class ProductBatchEntity
{
    public long Id { get; set; }
    public long ProductId { get; set; }
    public ProductEntity Product { get; set; } = null!;
    public string BatchNumber { get; set; } = string.Empty;
    public DateTime? ManufacturedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public decimal StockOnHand { get; set; }
    public bool IsBlocked { get; set; }
    public string? Notes { get; set; }
}
