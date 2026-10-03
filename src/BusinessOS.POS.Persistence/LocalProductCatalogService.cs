using System.Text.RegularExpressions;
using BusinessOS.POS.Application.Abstractions.Authentication;
using BusinessOS.POS.Application.Abstractions.Catalog;
using BusinessOS.POS.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.POS.Persistence;

public sealed class LocalProductCatalogService(
    IDbContextFactory<PosDbContext> contextFactory,
    IUserSessionService sessions,
    IPermissionAuthorizer authorizer)
    : IProductCatalogService
{
    public async Task<CatalogReferenceData> GetReferenceDataAsync(CancellationToken cancellationToken = default)
    {
        authorizer.Demand("inventory.view");
        var locale = RequireLocale();
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var categories = await context.Categories.AsNoTracking()
            .OrderBy(x => x.SortOrder).ThenBy(x => x.NameEn).ToListAsync(cancellationToken);
        var brands = await context.Brands.AsNoTracking()
            .OrderBy(x => x.NameEn).ToListAsync(cancellationToken);
        var units = await context.Units.AsNoTracking()
            .OrderBy(x => x.NameEn).ToListAsync(cancellationToken);

        return new CatalogReferenceData(
            categories.Select(x => MapCategory(x, locale)).ToList(),
            brands.Select(x => MapBrand(x, locale)).ToList(),
            units.Select(x => MapUnit(x, locale)).ToList());
    }

    public async Task<IReadOnlyList<CatalogProductSummary>> GetProductsAsync(
        string? search = null,
        long? categoryId = null,
        bool? isActive = null,
        CancellationToken cancellationToken = default)
    {
        authorizer.Demand("inventory.view");
        var locale = RequireLocale();
        search = search?.Trim();

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query = context.Products.AsNoTracking()
            .Include(x => x.Category)
            .Include(x => x.Brand)
            .Include(x => x.BaseUnit)
            .Include(x => x.Barcodes)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var like = "%" + search + "%";
            query = query.Where(x =>
                EF.Functions.Like(x.Sku, like) ||
                EF.Functions.Like(x.NameEn, like) ||
                (x.NameFa != null && EF.Functions.Like(x.NameFa, like)) ||
                (x.NamePs != null && EF.Functions.Like(x.NamePs, like)) ||
                x.Barcodes.Any(b => EF.Functions.Like(b.Barcode, like)));
        }

        if (categoryId is not null)
        {
            query = query.Where(x => x.CategoryId == categoryId);
        }

        if (isActive is not null)
        {
            query = query.Where(x => x.IsActive == isActive);
        }

        var products = await query.OrderBy(x => x.NameEn).Take(1000).ToListAsync(cancellationToken);
        return products.Select(x => new CatalogProductSummary(
            x.Id,
            x.Sku,
            Localize(x.NameEn, x.NameFa, x.NamePs, locale),
            x.Category is null ? null : Localize(x.Category.NameEn, x.Category.NameFa, x.Category.NamePs, locale),
            x.Brand is null ? null : Localize(x.Brand.NameEn, x.Brand.NameFa, x.Brand.NamePs, locale),
            Localize(x.BaseUnit.NameEn, x.BaseUnit.NameFa, x.BaseUnit.NamePs, locale),
            x.SellingPrice,
            x.StockOnHand,
            x.MinimumStock,
            x.Barcodes.Count,
            x.TrackStock,
            x.TrackExpiry,
            x.IsActive)).ToList();
    }

    public async Task<CatalogProductDetail?> GetProductAsync(long productId, CancellationToken cancellationToken = default)
    {
        authorizer.Demand("inventory.view");
        var locale = RequireLocale();
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var product = await context.Products.AsNoTracking()
            .Include(x => x.Units).ThenInclude(x => x.Unit)
            .Include(x => x.Barcodes).ThenInclude(x => x.ProductUnit).ThenInclude(x => x.Unit)
            .SingleOrDefaultAsync(x => x.Id == productId, cancellationToken);

        return product is null ? null : MapProductDetail(product, locale);
    }

    public async Task<CatalogProductDetail> SaveProductAsync(
        CatalogProductSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        authorizer.Demand("inventory.products.manage");
        ValidateText(request.Sku, 100, "SKU");
        ValidateText(request.NameEn, 200, "English product name");

        if (request.PurchaseCost < 0m || request.SellingPrice < 0m ||
            request.MinimumSellingPrice < 0m || request.WholesalePrice < 0m ||
            request.MinimumStock < 0m || request.ReorderQuantity < 0m)
        {
            throw new InvalidOperationException("Prices and stock thresholds cannot be negative.");
        }

        if (request.MinimumSellingPrice is not null && request.MinimumSellingPrice > request.SellingPrice)
        {
            throw new InvalidOperationException("Minimum selling price cannot be above the selling price.");
        }

        if (request.TrackExpiry && !request.TrackStock)
        {
            throw new InvalidOperationException("Expiry tracking requires stock tracking.");
        }

        var locale = RequireLocale();
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var sku = request.Sku.Trim();
        var duplicateSku = await context.Products.AnyAsync(
            x => x.Sku == sku && (!request.Id.HasValue || x.Id != request.Id.Value),
            cancellationToken);
        if (duplicateSku)
        {
            throw new InvalidOperationException("SKU must be unique.");
        }

        var baseUnit = await context.Units.SingleOrDefaultAsync(
            x => x.Id == request.BaseUnitId && x.IsActive, cancellationToken)
            ?? throw new InvalidOperationException("The selected base unit is unavailable.");

        ValidateQuantityPrecision(request.MinimumStock, baseUnit.DecimalPlaces, "Minimum stock");
        ValidateQuantityPrecision(request.ReorderQuantity, baseUnit.DecimalPlaces, "Reorder quantity");

        if (request.CategoryId is not null &&
            !await context.Categories.AnyAsync(x => x.Id == request.CategoryId && x.IsActive, cancellationToken))
        {
            throw new InvalidOperationException("The selected category is unavailable.");
        }

        if (request.BrandId is not null &&
            !await context.Brands.AnyAsync(x => x.Id == request.BrandId && x.IsActive, cancellationToken))
        {
            throw new InvalidOperationException("The selected brand is unavailable.");
        }

        var configuredUnitIds = new HashSet<long> { request.BaseUnitId };
        foreach (var unit in request.Units)
        {
            if (unit.UnitId == request.BaseUnitId || !configuredUnitIds.Add(unit.UnitId))
            {
                throw new InvalidOperationException("Each additional product unit must be unique and cannot repeat the base unit.");
            }

            if (unit.ConversionFactor <= 0m)
            {
                throw new InvalidOperationException("Unit conversion factor must be greater than zero.");
            }

            if (FractionalDigits(unit.ConversionFactor) > 6)
            {
                throw new InvalidOperationException("Unit conversion factor supports up to 6 decimal places.");
            }

            if (unit.SellingPrice < 0m || unit.MinimumSellingPrice < 0m || unit.WholesalePrice < 0m)
            {
                throw new InvalidOperationException("Unit prices cannot be negative.");
            }

            var effectiveSale = unit.SellingPrice ?? request.SellingPrice;
            if (unit.MinimumSellingPrice is not null && unit.MinimumSellingPrice > effectiveSale)
            {
                throw new InvalidOperationException("A unit minimum price cannot be above its selling price.");
            }
        }

        var activeUnitCount = await context.Units.CountAsync(
            x => configuredUnitIds.Contains(x.Id) && x.IsActive, cancellationToken);
        if (activeUnitCount != configuredUnitIds.Count)
        {
            throw new InvalidOperationException("One or more configured units are unavailable.");
        }

        var normalizedBarcodes = request.Barcodes
            .Select(x => new CatalogBarcodeInput(x.Barcode.Trim(), x.UnitId, x.IsPrimary))
            .ToList();

        if (normalizedBarcodes.Any(x => string.IsNullOrWhiteSpace(x.Barcode) || x.Barcode.Length > 191))
        {
            throw new InvalidOperationException("Barcode values are required and may contain at most 191 characters.");
        }

        if (normalizedBarcodes.Select(x => x.Barcode).Distinct(StringComparer.OrdinalIgnoreCase).Count() != normalizedBarcodes.Count)
        {
            throw new InvalidOperationException("Barcode values must be unique within the product.");
        }

        if (normalizedBarcodes.Count(x => x.IsPrimary) > 1)
        {
            throw new InvalidOperationException("Only one barcode can be primary.");
        }

        if (normalizedBarcodes.Any(x => !configuredUnitIds.Contains(x.UnitId)))
        {
            throw new InvalidOperationException("Every barcode must belong to a configured product unit.");
        }

        var barcodeValues = normalizedBarcodes.Select(x => x.Barcode).ToList();
        if (barcodeValues.Count > 0)
        {
            var duplicateBarcode = await context.ProductBarcodes.AnyAsync(
                x => barcodeValues.Contains(x.Barcode) &&
                     (!request.Id.HasValue || x.ProductId != request.Id.Value),
                cancellationToken);
            if (duplicateBarcode)
            {
                throw new InvalidOperationException("Barcode values must be unique across all products.");
            }
        }

        ProductEntity product;
        if (request.Id is null)
        {
            product = new ProductEntity { StockOnHand = 0m };
            context.Products.Add(product);
        }
        else
        {
            product = await context.Products
                .Include(x => x.Units)
                .Include(x => x.Barcodes)
                .SingleOrDefaultAsync(x => x.Id == request.Id.Value, cancellationToken)
                ?? throw new InvalidOperationException("Product was not found.");

            context.ProductBarcodes.RemoveRange(product.Barcodes);
            await context.SaveChangesAsync(cancellationToken);
            context.ProductUnits.RemoveRange(product.Units);
            await context.SaveChangesAsync(cancellationToken);
        }

        product.Sku = sku;
        product.NameEn = request.NameEn.Trim();
        product.NameFa = Clean(request.NameFa);
        product.NamePs = Clean(request.NamePs);
        product.CategoryId = request.CategoryId;
        product.BrandId = request.BrandId;
        product.BaseUnitId = request.BaseUnitId;
        product.DescriptionEn = Clean(request.DescriptionEn);
        product.DescriptionFa = Clean(request.DescriptionFa);
        product.DescriptionPs = Clean(request.DescriptionPs);
        product.ShelfLocation = Clean(request.ShelfLocation);
        product.PurchaseCost = Money(request.PurchaseCost);
        product.SellingPrice = Money(request.SellingPrice);
        product.MinimumSellingPrice = NullableMoney(request.MinimumSellingPrice);
        product.WholesalePrice = NullableMoney(request.WholesalePrice);
        product.MinimumStock = Quantity(request.MinimumStock);
        product.ReorderQuantity = Quantity(request.ReorderQuantity);
        product.TrackStock = request.TrackStock;
        product.TrackExpiry = request.TrackExpiry;
        product.IsActive = request.IsActive;

        await context.SaveChangesAsync(cancellationToken);

        var byUnitId = new Dictionary<long, ProductUnitEntity>();
        var baseProductUnit = new ProductUnitEntity
        {
            ProductId = product.Id,
            UnitId = request.BaseUnitId,
            ConversionFactor = 1m,
            CanPurchase = true,
            CanSell = true,
            SellingPrice = product.SellingPrice,
            MinimumSellingPrice = product.MinimumSellingPrice,
            WholesalePrice = product.WholesalePrice,
        };
        context.ProductUnits.Add(baseProductUnit);
        byUnitId[request.BaseUnitId] = baseProductUnit;

        foreach (var input in request.Units)
        {
            var entity = new ProductUnitEntity
            {
                ProductId = product.Id,
                UnitId = input.UnitId,
                ConversionFactor = Quantity(input.ConversionFactor),
                CanPurchase = input.CanPurchase,
                CanSell = input.CanSell,
                SellingPrice = NullableMoney(input.SellingPrice),
                MinimumSellingPrice = NullableMoney(input.MinimumSellingPrice),
                WholesalePrice = NullableMoney(input.WholesalePrice),
            };
            context.ProductUnits.Add(entity);
            byUnitId[input.UnitId] = entity;
        }

        await context.SaveChangesAsync(cancellationToken);

        if (normalizedBarcodes.Count > 0 && normalizedBarcodes.All(x => !x.IsPrimary))
        {
            normalizedBarcodes[0] = normalizedBarcodes[0] with { IsPrimary = true };
        }

        foreach (var barcode in normalizedBarcodes)
        {
            context.ProductBarcodes.Add(new ProductBarcodeEntity
            {
                ProductId = product.Id,
                ProductUnitId = byUnitId[barcode.UnitId].Id,
                Barcode = barcode.Barcode,
                IsPrimary = barcode.IsPrimary,
            });
        }

        context.AuditLogs.Add(new AuditLogEntity
        {
            ActorUserId = sessions.Current!.UserId,
            Event = request.Id is null ? "inventory.product.created" : "inventory.product.updated",
            CreatedAt = DateTimeOffset.UtcNow,
            DetailsJson = "{\"sku\":\"" + EscapeJson(product.Sku) + "\"}",
        });

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var saved = await context.Products.AsNoTracking()
            .Include(x => x.Units).ThenInclude(x => x.Unit)
            .Include(x => x.Barcodes).ThenInclude(x => x.ProductUnit).ThenInclude(x => x.Unit)
            .SingleAsync(x => x.Id == product.Id, cancellationToken);

        return MapProductDetail(saved, locale);
    }

    public async Task<CatalogCategoryItem> SaveCategoryAsync(
        CatalogCategorySaveRequest request,
        CancellationToken cancellationToken = default)
    {
        authorizer.Demand("inventory.catalog.manage");
        ValidateText(request.NameEn, 160, "English category name");
        if (request.SortOrder < 0)
        {
            throw new InvalidOperationException("Sort order cannot be negative.");
        }

        var locale = RequireLocale();
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        if (request.ParentId == request.Id && request.Id is not null)
        {
            throw new InvalidOperationException("A category cannot be its own parent.");
        }

        if (request.ParentId is not null &&
            !await context.Categories.AnyAsync(x => x.Id == request.ParentId && x.IsActive, cancellationToken))
        {
            throw new InvalidOperationException("The selected parent category is unavailable.");
        }

        CategoryEntity entity;
        if (request.Id is null)
        {
            entity = new CategoryEntity();
            context.Categories.Add(entity);
        }
        else
        {
            entity = await context.Categories.SingleOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
                ?? throw new InvalidOperationException("Category was not found.");
        }

        entity.ParentId = request.ParentId;
        entity.NameEn = request.NameEn.Trim();
        entity.NameFa = Clean(request.NameFa);
        entity.NamePs = Clean(request.NamePs);
        entity.SortOrder = request.SortOrder;
        entity.IsActive = request.IsActive;
        await AddAuditAsync(context, request.Id is null ? "inventory.category.created" : "inventory.category.updated", entity.NameEn);
        await context.SaveChangesAsync(cancellationToken);
        return MapCategory(entity, locale);
    }

    public async Task<CatalogBrandItem> SaveBrandAsync(
        CatalogBrandSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        authorizer.Demand("inventory.catalog.manage");
        ValidateText(request.NameEn, 160, "English brand name");
        var locale = RequireLocale();
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var normalized = request.NameEn.Trim();
        var existing = await context.Brands.AsNoTracking().ToListAsync(cancellationToken);
        if (existing.Any(x => x.Id != request.Id &&
            string.Equals(x.NameEn, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Brand English name must be unique.");
        }

        BrandEntity entity;
        if (request.Id is null)
        {
            entity = new BrandEntity();
            context.Brands.Add(entity);
        }
        else
        {
            entity = await context.Brands.SingleOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
                ?? throw new InvalidOperationException("Brand was not found.");
        }

        entity.NameEn = normalized;
        entity.NameFa = Clean(request.NameFa);
        entity.NamePs = Clean(request.NamePs);
        entity.IsActive = request.IsActive;
        await AddAuditAsync(context, request.Id is null ? "inventory.brand.created" : "inventory.brand.updated", entity.NameEn);
        await context.SaveChangesAsync(cancellationToken);
        return MapBrand(entity, locale);
    }

    public async Task<CatalogUnitItem> SaveUnitAsync(
        CatalogUnitSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        authorizer.Demand("inventory.catalog.manage");
        ValidateText(request.Code, 30, "Unit code");
        ValidateText(request.NameEn, 100, "English unit name");

        var code = request.Code.Trim().ToUpperInvariant();
        if (!Regex.IsMatch(code, "^[A-Z0-9_-]+$", RegexOptions.CultureInvariant))
        {
            throw new InvalidOperationException("Unit code may contain only ASCII letters, numbers, underscores, and hyphens.");
        }

        if (request.DecimalPlaces is < 0 or > 6)
        {
            throw new InvalidOperationException("Unit decimal places must be between 0 and 6.");
        }

        var locale = RequireLocale();
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (await context.Units.AnyAsync(x => x.Code == code && (!request.Id.HasValue || x.Id != request.Id), cancellationToken))
        {
            throw new InvalidOperationException("Unit code must be unique.");
        }

        UnitEntity entity;
        if (request.Id is null)
        {
            entity = new UnitEntity();
            context.Units.Add(entity);
        }
        else
        {
            entity = await context.Units.SingleOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
                ?? throw new InvalidOperationException("Unit was not found.");
        }

        entity.Code = code;
        entity.NameEn = request.NameEn.Trim();
        entity.NameFa = Clean(request.NameFa);
        entity.NamePs = Clean(request.NamePs);
        entity.Symbol = Clean(request.Symbol);
        entity.DecimalPlaces = request.DecimalPlaces;
        entity.IsActive = request.IsActive;
        await AddAuditAsync(context, request.Id is null ? "inventory.unit.created" : "inventory.unit.updated", entity.Code);
        await context.SaveChangesAsync(cancellationToken);
        return MapUnit(entity, locale);
    }

    private string RequireLocale() =>
        sessions.Current?.PreferredLocale ?? throw new InvalidOperationException("No user is signed in.");

    private async Task AddAuditAsync(PosDbContext context, string eventName, string value)
    {
        context.AuditLogs.Add(new AuditLogEntity
        {
            ActorUserId = sessions.Current!.UserId,
            Event = eventName,
            CreatedAt = DateTimeOffset.UtcNow,
            DetailsJson = "{\"value\":\"" + EscapeJson(value) + "\"}",
        });
        await Task.CompletedTask;
    }

    private static CatalogProductDetail MapProductDetail(ProductEntity product, string locale) =>
        new(
            product.Id,
            product.Sku,
            product.NameEn,
            product.NameFa,
            product.NamePs,
            product.CategoryId,
            product.BrandId,
            product.BaseUnitId,
            product.DescriptionEn,
            product.DescriptionFa,
            product.DescriptionPs,
            product.ShelfLocation,
            product.PurchaseCost,
            product.SellingPrice,
            product.MinimumSellingPrice,
            product.WholesalePrice,
            product.StockOnHand,
            product.MinimumStock,
            product.ReorderQuantity,
            product.TrackStock,
            product.TrackExpiry,
            product.IsActive,
            product.Units.OrderBy(x => x.Id).Select(x => new CatalogProductUnitDetail(
                x.Id, x.UnitId, Localize(x.Unit.NameEn, x.Unit.NameFa, x.Unit.NamePs, locale),
                x.Unit.Code, x.ConversionFactor, x.CanPurchase, x.CanSell,
                x.SellingPrice, x.MinimumSellingPrice, x.WholesalePrice)).ToList(),
            product.Barcodes.OrderByDescending(x => x.IsPrimary).ThenBy(x => x.Id).Select(x =>
                new CatalogBarcodeDetail(
                    x.Id, x.ProductUnitId, x.ProductUnit.UnitId,
                    Localize(x.ProductUnit.Unit.NameEn, x.ProductUnit.Unit.NameFa, x.ProductUnit.Unit.NamePs, locale),
                    x.Barcode, x.IsPrimary)).ToList());

    private static CatalogCategoryItem MapCategory(CategoryEntity x, string locale) =>
        new(x.Id, x.ParentId, Localize(x.NameEn, x.NameFa, x.NamePs, locale), x.NameEn, x.NameFa, x.NamePs, x.SortOrder, x.IsActive);

    private static CatalogBrandItem MapBrand(BrandEntity x, string locale) =>
        new(x.Id, Localize(x.NameEn, x.NameFa, x.NamePs, locale), x.NameEn, x.NameFa, x.NamePs, x.IsActive);

    private static CatalogUnitItem MapUnit(UnitEntity x, string locale) =>
        new(x.Id, x.Code, Localize(x.NameEn, x.NameFa, x.NamePs, locale), x.NameEn, x.NameFa, x.NamePs, x.Symbol, x.DecimalPlaces, x.IsActive);

    private static string Localize(string en, string? fa, string? ps, string locale) =>
        locale switch
        {
            "fa" when !string.IsNullOrWhiteSpace(fa) => fa,
            "ps" when !string.IsNullOrWhiteSpace(ps) => ps,
            _ => en,
        };

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static decimal Money(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    private static decimal? NullableMoney(decimal? value) => value is null ? null : Money(value.Value);
    private static decimal Quantity(decimal value) => decimal.Round(value, 6, MidpointRounding.AwayFromZero);

    private static void ValidateText(string? value, int maxLength, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(field + " is required.");
        }

        if (value.Trim().Length > maxLength)
        {
            throw new InvalidOperationException(field + " may contain at most " + maxLength + " characters.");
        }
    }

    private static void ValidateQuantityPrecision(decimal value, int places, string field)
    {
        if (FractionalDigits(value) > places)
        {
            throw new InvalidOperationException(field + " exceeds the base unit precision of " + places + " decimal places.");
        }
    }

    private static int FractionalDigits(decimal value)
    {
        value = Math.Abs(value);
        var bits = decimal.GetBits(value);
        return (bits[3] >> 16) & 0x7F;
    }

    private static string EscapeJson(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
}
