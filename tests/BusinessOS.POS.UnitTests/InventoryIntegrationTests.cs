using BusinessOS.POS.Application.Abstractions.Authentication;
using BusinessOS.POS.Application.Abstractions.Catalog;
using BusinessOS.POS.Application.Abstractions.Inventory;
using BusinessOS.POS.Application.Abstractions.Persistence;
using BusinessOS.POS.Application.Abstractions.Sales;
using BusinessOS.POS.Application.Abstractions.Storage;
using BusinessOS.POS.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BusinessOS.POS.UnitTests;

public sealed class InventoryIntegrationTests
{
    [Fact]
    public async Task Expiry_stock_uses_fefo_physical_depletion_while_cogs_remains_fifo()
    {
        var root = Path.Combine(Path.GetTempPath(), "BusinessOS-POS-inventory-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            await using var provider = await BuildProviderAsync(root);
            var catalog = provider.GetRequiredService<IProductCatalogService>();
            var inventory = provider.GetRequiredService<IInventoryService>();
            var pos = provider.GetRequiredService<IPosService>();

            var refs = await catalog.GetReferenceDataAsync();
            var pcs = refs.Units.First(x => x.Code == "PCS");

            var product = await catalog.SaveProductAsync(new CatalogProductSaveRequest(
                null,
                "EXP-FEFO-001",
                "Expiry FEFO Product",
                "محصول آزمایشی تاریخ",
                "د نېټې ازمایښتي محصول",
                null,
                null,
                pcs.Id,
                null,
                null,
                null,
                "EXP-01",
                10m,
                40m,
                35m,
                null,
                1m,
                5m,
                true,
                true,
                true,
                [],
                [new CatalogBarcodeInput("9900000000001", pcs.Id, true)]));

            var lateExpiry = DateTime.Today.AddDays(30);
            var earlyExpiry = DateTime.Today.AddDays(10);

            await inventory.RecordOpeningStockAsync(new OpeningStockRequest(
                Guid.NewGuid().ToString(), product.Id, pcs.Id, 5m, 10m,
                "LATE-001", DateTime.Today.AddDays(-2), lateExpiry, "First FIFO cost layer"));

            await inventory.RecordOpeningStockAsync(new OpeningStockRequest(
                Guid.NewGuid().ToString(), product.Id, pcs.Id, 5m, 20m,
                "EARLY-001", DateTime.Today.AddDays(-1), earlyExpiry, "Second FIFO cost layer"));

            var before = await inventory.GetReferenceDataAsync();
            var stocked = before.Products.Single(x => x.ProductId == product.Id);
            Assert.Equal(10m, stocked.StockOnHand);
            Assert.Equal(5m, stocked.Batches.Single(x => x.BatchNumber == "EARLY-001").StockOnHand);
            Assert.Equal(5m, stocked.Batches.Single(x => x.BatchNumber == "LATE-001").StockOnHand);

            await pos.OpenShiftAsync(500m);
            var sellable = (await pos.SearchProductsAsync("9900000000001")).Single();
            Assert.Equal(10m, sellable.AvailableQuantity);

            var sale = await pos.CheckoutAsync(new PosCheckoutRequest(
                Guid.NewGuid().ToString(),
                [new PosCheckoutLineRequest(sellable.ProductUnitId, 2m)],
                0m,
                [new PosPaymentRequest("cash", sellable.Price * 2m)]));

            Assert.Equal(20m, sale.CogsTotal);

            var after = await inventory.GetReferenceDataAsync();
            var remaining = after.Products.Single(x => x.ProductId == product.Id);
            Assert.Equal(8m, remaining.StockOnHand);
            Assert.Equal(3m, remaining.Batches.Single(x => x.BatchNumber == "EARLY-001").StockOnHand);
            Assert.Equal(5m, remaining.Batches.Single(x => x.BatchNumber == "LATE-001").StockOnHand);

            var movements = await inventory.GetMovementsAsync(product.Id);
            var saleMovement = movements.First(x => x.MovementType == "sale");
            Assert.Equal("EARLY-001", saleMovement.BatchNumber);
            Assert.Equal(-2m, saleMovement.QuantityBase);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Inventory_counts_adjustments_and_expiry_writeoffs_protect_stock_integrity()
    {
        var root = Path.Combine(Path.GetTempPath(), "BusinessOS-POS-inventory-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            await using var provider = await BuildProviderAsync(root);
            var catalog = provider.GetRequiredService<IProductCatalogService>();
            var inventory = provider.GetRequiredService<IInventoryService>();

            var refs = await catalog.GetReferenceDataAsync();
            var pcs = refs.Units.First(x => x.Code == "PCS");

            var normal = await catalog.SaveProductAsync(new CatalogProductSaveRequest(
                null, "COUNT-001", "Count Product", null, null, null, null, pcs.Id,
                null, null, null, null, 15m, 25m, 20m, null,
                2m, 4m, true, false, true, [], []));

            await inventory.RecordOpeningStockAsync(new OpeningStockRequest(
                Guid.NewGuid().ToString(), normal.Id, pcs.Id, 10m, 15m,
                null, null, null, "Opening count stock"));

            var target = (await inventory.GetReferenceDataAsync()).Targets
                .Single(x => x.ProductId == normal.Id && x.BatchId is null);

            var draft = await inventory.CreateStockCountAsync(new CreateStockCountRequest(
                Guid.NewGuid().ToString(),
                [new StockCountLineRequest(target.Key, 8m)],
                "Cycle count"));

            Assert.Equal("draft", draft.Status);
            Assert.Equal(-2m, draft.Items.Single().VarianceQuantityBase);

            await inventory.AdjustStockAsync(new StockAdjustmentRequest(
                Guid.NewGuid().ToString(),
                target.Key,
                1m,
                true,
                "Test stock changed after count",
                null));

            var stale = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                inventory.ApproveStockCountAsync(draft.Id, Guid.NewGuid().ToString()));
            Assert.Contains("Stock changed after count", stale.Message);

            var refreshedTarget = (await inventory.GetReferenceDataAsync()).Targets
                .Single(x => x.ProductId == normal.Id && x.BatchId is null);
            var recount = await inventory.CreateStockCountAsync(new CreateStockCountRequest(
                Guid.NewGuid().ToString(),
                [new StockCountLineRequest(refreshedTarget.Key, 9m)],
                "Recount after adjustment"));

            var approved = await inventory.ApproveStockCountAsync(recount.Id, Guid.NewGuid().ToString());
            Assert.Equal("approved", approved.Status);
            Assert.Equal(-2m, approved.Items.Single().VarianceQuantityBase);

            var expiry = await catalog.SaveProductAsync(new CatalogProductSaveRequest(
                null, "EXPIRED-001", "Expired Product", null, null, null, null, pcs.Id,
                null, null, null, null, 12m, 30m, 25m, null,
                0m, 0m, true, true, true, [], []));

            await inventory.RecordOpeningStockAsync(new OpeningStockRequest(
                Guid.NewGuid().ToString(), expiry.Id, pcs.Id, 4m, 12m,
                "OLD-LOT", DateTime.Today.AddDays(-60), DateTime.Today.AddDays(-1), "Expired test lot"));

            var expiryTarget = (await inventory.GetReferenceDataAsync()).Targets
                .Single(x => x.ProductId == expiry.Id && x.BatchId is not null);

            var writeoff = await inventory.PostWriteoffAsync(new InventoryWriteoffRequest(
                Guid.NewGuid().ToString(),
                "expiry",
                "Expired on shelf",
                [new InventoryWriteoffLineRequest(expiryTarget.Key, 4m)],
                "Test expiry write-off"));

            Assert.StartsWith("EXP-", writeoff.Number);
            Assert.Equal(4m, writeoff.TotalQuantity);
            Assert.Equal(48m, writeoff.TotalCost);

            var stock = (await inventory.GetStockAsync("EXPIRED-001")).Single();
            Assert.Equal(0m, stock.StockOnHand);
            Assert.Equal(0m, stock.ExpiredQuantity);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<ServiceProvider> BuildProviderAsync(string root)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IApplicationPaths>(new TestPaths(root));
        services.AddBusinessOSPosPersistence();
        var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<ILocalDatabaseInitializer>().InitializeAsync();
        await provider.GetRequiredService<IOwnerBootstrapService>()
            .CreateOwnerAsync("Inventory Owner", "owner", "Password-123", "en");
        await provider.GetRequiredService<IUserSessionService>()
            .LoginAsync("owner", "Password-123");

        return provider;
    }

    private sealed class TestPaths(string root) : IApplicationPaths
    {
        public string RootPath { get; } = root;
        public string DatabasePath { get; } = Path.Combine(root, "inventory.db");
        public void EnsureCreated() => Directory.CreateDirectory(RootPath);
    }
}
