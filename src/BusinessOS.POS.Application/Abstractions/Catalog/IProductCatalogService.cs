namespace BusinessOS.POS.Application.Abstractions.Catalog;

public interface IProductCatalogService
{
    Task<CatalogReferenceData> GetReferenceDataAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CatalogProductSummary>> GetProductsAsync(
        string? search = null,
        long? categoryId = null,
        bool? isActive = null,
        CancellationToken cancellationToken = default);
    Task<CatalogProductDetail?> GetProductAsync(long productId, CancellationToken cancellationToken = default);
    Task<CatalogProductDetail> SaveProductAsync(CatalogProductSaveRequest request, CancellationToken cancellationToken = default);
    Task<CatalogCategoryItem> SaveCategoryAsync(CatalogCategorySaveRequest request, CancellationToken cancellationToken = default);
    Task<CatalogBrandItem> SaveBrandAsync(CatalogBrandSaveRequest request, CancellationToken cancellationToken = default);
    Task<CatalogUnitItem> SaveUnitAsync(CatalogUnitSaveRequest request, CancellationToken cancellationToken = default);
}

public sealed record CatalogReferenceData(
    IReadOnlyList<CatalogCategoryItem> Categories,
    IReadOnlyList<CatalogBrandItem> Brands,
    IReadOnlyList<CatalogUnitItem> Units);

public sealed record CatalogCategoryItem(
    long Id,
    long? ParentId,
    string Name,
    string NameEn,
    string? NameFa,
    string? NamePs,
    int SortOrder,
    bool IsActive);

public sealed record CatalogBrandItem(
    long Id,
    string Name,
    string NameEn,
    string? NameFa,
    string? NamePs,
    bool IsActive);

public sealed record CatalogUnitItem(
    long Id,
    string Code,
    string Name,
    string NameEn,
    string? NameFa,
    string? NamePs,
    string? Symbol,
    int DecimalPlaces,
    bool IsActive);

public sealed record CatalogProductSummary(
    long Id,
    string Sku,
    string Name,
    string? Category,
    string? Brand,
    string BaseUnit,
    decimal SellingPrice,
    decimal StockOnHand,
    decimal MinimumStock,
    int BarcodeCount,
    bool TrackStock,
    bool TrackExpiry,
    bool IsActive);

public sealed record CatalogProductUnitDetail(
    long Id,
    long UnitId,
    string UnitName,
    string UnitCode,
    decimal ConversionFactor,
    bool CanPurchase,
    bool CanSell,
    decimal? SellingPrice,
    decimal? MinimumSellingPrice,
    decimal? WholesalePrice);

public sealed record CatalogBarcodeDetail(
    long Id,
    long ProductUnitId,
    long UnitId,
    string UnitName,
    string Barcode,
    bool IsPrimary);

public sealed record CatalogProductDetail(
    long Id,
    string Sku,
    string NameEn,
    string? NameFa,
    string? NamePs,
    long? CategoryId,
    long? BrandId,
    long BaseUnitId,
    string? DescriptionEn,
    string? DescriptionFa,
    string? DescriptionPs,
    string? ShelfLocation,
    decimal PurchaseCost,
    decimal SellingPrice,
    decimal? MinimumSellingPrice,
    decimal? WholesalePrice,
    decimal StockOnHand,
    decimal MinimumStock,
    decimal ReorderQuantity,
    bool TrackStock,
    bool TrackExpiry,
    bool IsActive,
    IReadOnlyList<CatalogProductUnitDetail> Units,
    IReadOnlyList<CatalogBarcodeDetail> Barcodes);

public sealed record CatalogProductUnitInput(
    long UnitId,
    decimal ConversionFactor,
    bool CanPurchase,
    bool CanSell,
    decimal? SellingPrice,
    decimal? MinimumSellingPrice,
    decimal? WholesalePrice);

public sealed record CatalogBarcodeInput(
    string Barcode,
    long UnitId,
    bool IsPrimary);

public sealed record CatalogProductSaveRequest(
    long? Id,
    string Sku,
    string NameEn,
    string? NameFa,
    string? NamePs,
    long? CategoryId,
    long? BrandId,
    long BaseUnitId,
    string? DescriptionEn,
    string? DescriptionFa,
    string? DescriptionPs,
    string? ShelfLocation,
    decimal PurchaseCost,
    decimal SellingPrice,
    decimal? MinimumSellingPrice,
    decimal? WholesalePrice,
    decimal MinimumStock,
    decimal ReorderQuantity,
    bool TrackStock,
    bool TrackExpiry,
    bool IsActive,
    IReadOnlyList<CatalogProductUnitInput> Units,
    IReadOnlyList<CatalogBarcodeInput> Barcodes);

public sealed record CatalogCategorySaveRequest(
    long? Id,
    long? ParentId,
    string NameEn,
    string? NameFa,
    string? NamePs,
    int SortOrder,
    bool IsActive);

public sealed record CatalogBrandSaveRequest(
    long? Id,
    string NameEn,
    string? NameFa,
    string? NamePs,
    bool IsActive);

public sealed record CatalogUnitSaveRequest(
    long? Id,
    string Code,
    string NameEn,
    string? NameFa,
    string? NamePs,
    string? Symbol,
    int DecimalPlaces,
    bool IsActive);
