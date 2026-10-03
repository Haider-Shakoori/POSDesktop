using BusinessOS.POS.Application.Abstractions.Authentication;
using BusinessOS.POS.Application.Abstractions.Catalog;
using BusinessOS.POS.Application.Abstractions.Persistence;
using BusinessOS.POS.Application.Abstractions.Sales;
using BusinessOS.POS.Application.Abstractions.Storage;
using BusinessOS.POS.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BusinessOS.POS.UnitTests;

public sealed class ProductCatalogIntegrationTests
{
    [Fact]
    public async Task Product_catalog_supports_categories_brands_units_barcodes_and_pos_lookup()
    {
        var root = Path.Combine(Path.GetTempPath(), "BusinessOS-POS-catalog-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var services = new ServiceCollection();
            services.AddSingleton<IApplicationPaths>(new TestPaths(root));
            services.AddBusinessOSPosPersistence();
            await using var provider = services.BuildServiceProvider();

            await provider.GetRequiredService<ILocalDatabaseInitializer>().InitializeAsync();
            await provider.GetRequiredService<IOwnerBootstrapService>()
                .CreateOwnerAsync("Catalog Owner", "owner", "Password-123", "en");
            await provider.GetRequiredService<IUserSessionService>()
                .LoginAsync("owner", "Password-123");

            var catalog = provider.GetRequiredService<IProductCatalogService>();
            var references = await catalog.GetReferenceDataAsync();

            Assert.True(references.Categories.Count >= 5);
            Assert.Contains(references.Units, x => x.Code == "PCS");
            Assert.Contains(references.Units, x => x.Code == "BOX");

            var beverages = references.Categories.First(x => x.NameEn == "Beverages");
            var brand = await catalog.SaveBrandAsync(new CatalogBrandSaveRequest(
                null, "Kabul Test Brand", "برند آزمایشی کابل", "کابل ازمایښتي برانډ", true));

            var tray = await catalog.SaveUnitAsync(new CatalogUnitSaveRequest(
                null, "tray", "Tray", "سینی", "پتنوس", "tray", 0, true));
            Assert.Equal("TRAY", tray.Code);

            references = await catalog.GetReferenceDataAsync();
            var pcs = references.Units.First(x => x.Code == "PCS");
            var box = references.Units.First(x => x.Code == "BOX");

            var created = await catalog.SaveProductAsync(new CatalogProductSaveRequest(
                null,
                "TEST-COLA-001",
                "Test Cola",
                "نوشابه آزمایشی",
                "ازمایښتي کولا",
                beverages.Id,
                brand.Id,
                pcs.Id,
                "Desktop catalog integration product",
                null,
                null,
                "A-01",
                20m,
                30m,
                25m,
                27m,
                4m,
                12m,
                true,
                false,
                true,
                [new CatalogProductUnitInput(box.Id, 12m, true, true, 340m, 300m, 320m)],
                [
                    new CatalogBarcodeInput("9876543210001", pcs.Id, false),
                    new CatalogBarcodeInput("9876543210018", box.Id, false),
                ]));

            Assert.Equal("TEST-COLA-001", created.Sku);
            Assert.Equal(2, created.Units.Count);
            Assert.Equal(2, created.Barcodes.Count);
            Assert.Single(created.Barcodes.Where(x => x.IsPrimary));
            Assert.Equal(pcs.Id, created.BaseUnitId);

            var found = await catalog.GetProductsAsync("9876543210001");
            Assert.Single(found);
            Assert.Equal(created.Id, found[0].Id);
            Assert.Equal("Beverages", found[0].Category);
            Assert.Equal("Kabul Test Brand", found[0].Brand);

            var pos = provider.GetRequiredService<IPosService>();
            var posLookup = await pos.SearchProductsAsync("9876543210001");
            Assert.Single(posLookup);
            Assert.Equal("TEST-COLA-001", posLookup[0].Sku);
            Assert.Equal(pcs.Id, created.Barcodes.First(x => x.IsPrimary).UnitId);

            var duplicateBarcode = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                catalog.SaveProductAsync(new CatalogProductSaveRequest(
                    null, "TEST-COLA-002", "Second Cola", null, null,
                    beverages.Id, brand.Id, pcs.Id, null, null, null, null,
                    20m, 30m, 25m, null, 0m, 0m, true, false, true, [],
                    [new CatalogBarcodeInput("9876543210001", pcs.Id, true)])));
            Assert.Contains("unique across all products", duplicateBarcode.Message);

            var updated = await catalog.SaveProductAsync(new CatalogProductSaveRequest(
                created.Id,
                created.Sku,
                created.NameEn,
                created.NameFa,
                created.NamePs,
                created.CategoryId,
                created.BrandId,
                created.BaseUnitId,
                created.DescriptionEn,
                created.DescriptionFa,
                created.DescriptionPs,
                created.ShelfLocation,
                created.PurchaseCost,
                32m,
                25m,
                created.WholesalePrice,
                created.MinimumStock,
                created.ReorderQuantity,
                created.TrackStock,
                created.TrackExpiry,
                false,
                created.Units.Where(x => x.UnitId != created.BaseUnitId)
                    .Select(x => new CatalogProductUnitInput(
                        x.UnitId, x.ConversionFactor, x.CanPurchase, x.CanSell,
                        x.SellingPrice, x.MinimumSellingPrice, x.WholesalePrice)).ToList(),
                created.Barcodes.Select(x => new CatalogBarcodeInput(
                    x.Barcode, x.UnitId, x.IsPrimary)).ToList()));

            Assert.False(updated.IsActive);
            Assert.Equal(32m, updated.SellingPrice);
            Assert.Single(await catalog.GetProductsAsync("TEST-COLA-001", isActive: false));
            Assert.Empty(await pos.SearchProductsAsync("9876543210001"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Product_validation_preserves_web_catalog_rules()
    {
        var root = Path.Combine(Path.GetTempPath(), "BusinessOS-POS-catalog-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var services = new ServiceCollection();
            services.AddSingleton<IApplicationPaths>(new TestPaths(root));
            services.AddBusinessOSPosPersistence();
            await using var provider = services.BuildServiceProvider();

            await provider.GetRequiredService<ILocalDatabaseInitializer>().InitializeAsync();
            await provider.GetRequiredService<IOwnerBootstrapService>()
                .CreateOwnerAsync("Catalog Owner", "owner", "Password-123", "en");
            await provider.GetRequiredService<IUserSessionService>()
                .LoginAsync("owner", "Password-123");

            var catalog = provider.GetRequiredService<IProductCatalogService>();
            var refs = await catalog.GetReferenceDataAsync();
            var pcs = refs.Units.First(x => x.Code == "PCS");
            var box = refs.Units.First(x => x.Code == "BOX");

            var expiryWithoutStock = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                catalog.SaveProductAsync(new CatalogProductSaveRequest(
                    null, "RULE-001", "Invalid Expiry", null, null, null, null, pcs.Id,
                    null, null, null, null, 0m, 10m, null, null,
                    0m, 0m, false, true, true, [], [])));
            Assert.Contains("requires stock tracking", expiryWithoutStock.Message);

            var duplicateUnit = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                catalog.SaveProductAsync(new CatalogProductSaveRequest(
                    null, "RULE-002", "Duplicate Unit", null, null, null, null, pcs.Id,
                    null, null, null, null, 0m, 10m, null, null,
                    0m, 0m, true, false, true,
                    [
                        new CatalogProductUnitInput(box.Id, 12m, false, true, null, null, null),
                        new CatalogProductUnitInput(box.Id, 24m, false, true, null, null, null),
                    ],
                    [])));
            Assert.Contains("unique", duplicateUnit.Message);

            var badPrimary = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                catalog.SaveProductAsync(new CatalogProductSaveRequest(
                    null, "RULE-003", "Two Primary", null, null, null, null, pcs.Id,
                    null, null, null, null, 0m, 10m, null, null,
                    0m, 0m, true, false, true, [],
                    [
                        new CatalogBarcodeInput("1111111111111", pcs.Id, true),
                        new CatalogBarcodeInput("2222222222222", pcs.Id, true),
                    ])));
            Assert.Contains("Only one barcode", badPrimary.Message);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private sealed class TestPaths(string root) : IApplicationPaths
    {
        public string RootPath { get; } = root;
        public string DatabasePath { get; } = Path.Combine(root, "catalog.db");
        public void EnsureCreated() => Directory.CreateDirectory(RootPath);
    }
}
