using BusinessOS.POS.Application.Abstractions.Authentication;
using BusinessOS.POS.Application.Abstractions.Catalog;
using BusinessOS.POS.Application.Abstractions.Persistence;
using BusinessOS.POS.Application.Abstractions.Purchasing;
using BusinessOS.POS.Application.Abstractions.Sales;
using BusinessOS.POS.Application.Abstractions.Storage;
using BusinessOS.POS.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BusinessOS.POS.UnitTests;

public sealed class PurchasingIntegrationTests
{
    [Fact]
    public async Task Supplier_opening_balance_creates_immutable_payable_entry()
    {
        var root = NewRoot();
        try
        {
            await using var provider = await BuildProviderAsync(root);
            var purchasing = provider.GetRequiredService<IPurchasingService>();

            var supplier = await purchasing.SaveSupplierAsync(new SupplierSaveRequest(
                null, "Opening Supplier", "Ahmad", "0700000000", null,
                "Kabul", 250m, "Opening payable", true));

            Assert.Equal(250m, supplier.CurrentBalance);
            var detail = await purchasing.GetSupplierAsync(supplier.Id);
            Assert.NotNull(detail);
            var entry = Assert.Single(detail!.Ledger);
            Assert.Equal("opening_balance", entry.EntryType);
            Assert.Equal(0m, entry.Debit);
            Assert.Equal(250m, entry.Credit);
            Assert.Equal(250m, entry.BalanceAfter);

            var changedOpening = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                purchasing.SaveSupplierAsync(new SupplierSaveRequest(
                    supplier.Id, supplier.Name, supplier.ContactPerson, supplier.Phone,
                    supplier.AlternatePhone, supplier.Address, 300m, supplier.Notes, true)));
            Assert.Contains("immutable", changedOpening.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public async Task Purchase_order_receiving_updates_stock_landed_cost_payable_and_order_status()
    {
        var root = NewRoot();
        try
        {
            await using var provider = await BuildProviderAsync(root);
            var purchasing = provider.GetRequiredService<IPurchasingService>();
            var product = await CreateProductAsync(provider, "PO-LANDED", false);
            var supplier = await CreateSupplierAsync(purchasing, "PO Supplier");

            var order = await purchasing.CreatePurchaseOrderAsync(new PurchaseOrderCreateRequest(
                supplier.Id, DateTime.Today, DateTime.Today.AddDays(2), "SUP-PO-1",
                0m, "Desktop PO",
                [new PurchaseOrderLineRequest(product.ProductUnitId, 10m, 10m)]));
            Assert.Equal("draft", order.Header.Status);

            order = await purchasing.ApprovePurchaseOrderAsync(order.Header.Id);
            Assert.Equal("approved", order.Header.Status);

            var first = await purchasing.PostGoodsReceiptAsync(new GoodsReceiptPostRequest(
                Guid.NewGuid().ToString(), supplier.Id, order.Header.Id, "INV-1",
                DateTimeOffset.UtcNow, 0m, 0m, null, null, null, "First partial receipt",
                [new GoodsReceiptLineRequest(order.Items[0].Id, null, 4m, null)],
                [new GoodsReceiptExpenseRequest("transport", 8m, "Delivery")]));

            Assert.Equal(48m, first.Header.NetTotal);
            Assert.Equal(48m, first.Header.BalanceDue);
            Assert.Equal(4m, first.Items[0].QuantityBase);
            Assert.Equal(12m, first.Items[0].BaseUnitLandedCost);

            var afterFirst = await purchasing.GetPurchaseOrderAsync(order.Header.Id);
            Assert.Equal("partially_received", afterFirst!.Header.Status);
            Assert.Equal(6m, afterFirst.Items[0].RemainingQuantity);

            var second = await purchasing.PostGoodsReceiptAsync(new GoodsReceiptPostRequest(
                Guid.NewGuid().ToString(), supplier.Id, order.Header.Id, "INV-2",
                DateTimeOffset.UtcNow, 0m, 0m, null, null, null, "Final receipt",
                [new GoodsReceiptLineRequest(order.Items[0].Id, null, 6m, null)],
                []));

            Assert.Equal(60m, second.Header.NetTotal);
            var afterSecond = await purchasing.GetPurchaseOrderAsync(order.Header.Id);
            Assert.Equal("received", afterSecond!.Header.Status);
            Assert.Equal(0m, afterSecond.Items[0].RemainingQuantity);

            var factory = provider.GetRequiredService<IDbContextFactory<PosDbContext>>();
            await using var context = await factory.CreateDbContextAsync();
            var productDb = await context.Products.AsNoTracking().SingleAsync(x => x.Id == product.ProductId);
            Assert.Equal(10m, productDb.StockOnHand);
            Assert.Equal(10m, productDb.PurchaseCost);
            Assert.Equal(2, await context.InventoryCostLayers.CountAsync(x => x.ProductId == product.ProductId));

            var supplierDb = await context.Suppliers.AsNoTracking().SingleAsync(x => x.Id == supplier.Id);
            Assert.Equal(108m, supplierDb.CurrentBalance);
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public async Task Initial_purchase_payment_and_supplier_payment_allocate_oldest_receipts_first()
    {
        var root = NewRoot();
        try
        {
            await using var provider = await BuildProviderAsync(root);
            var purchasing = provider.GetRequiredService<IPurchasingService>();
            var pos = provider.GetRequiredService<IPosService>();
            var product = await CreateProductAsync(provider, "PAYABLE", false);
            var supplier = await CreateSupplierAsync(purchasing, "Payment Supplier");
            await pos.OpenShiftAsync(1000m);

            var first = await DirectReceiptAsync(purchasing, supplier.Id, product.ProductUnitId, 10m, 10m, 30m, "cash");
            var second = await DirectReceiptAsync(purchasing, supplier.Id, product.ProductUnitId, 5m, 10m, 0m, null);

            Assert.Equal(70m, first.Header.BalanceDue);
            Assert.Equal(50m, second.Header.BalanceDue);

            var key = Guid.NewGuid().ToString();
            var payment = await purchasing.RecordSupplierPaymentAsync(new SupplierPaymentRequest(
                key, supplier.Id, 90m, "cash", "PAY-90", null, "Oldest first"));

            Assert.Equal(2, payment.Allocations.Count);
            Assert.Equal(first.Header.Id, payment.Allocations[0].GoodsReceiptId);
            Assert.Equal(70m, payment.Allocations[0].Amount);
            Assert.Equal(second.Header.Id, payment.Allocations[1].GoodsReceiptId);
            Assert.Equal(20m, payment.Allocations[1].Amount);
            Assert.Equal(30m, payment.SupplierBalanceAfter);

            var retry = await purchasing.RecordSupplierPaymentAsync(new SupplierPaymentRequest(
                key, supplier.Id, 90m, "cash", "PAY-90", null, "Oldest first"));
            Assert.Equal(payment.Id, retry.Id);

            var supplierDetail = await purchasing.GetSupplierAsync(supplier.Id);
            Assert.Equal(30m, supplierDetail!.Supplier.CurrentBalance);
            Assert.Single(supplierDetail.OpenReceipts);
            Assert.Equal(second.Header.Id, supplierDetail.OpenReceipts[0].Id);
            Assert.Equal(30m, supplierDetail.OpenReceipts[0].BalanceDue);
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public async Task Purchase_return_reverses_exact_stock_cost_layer_and_supplier_payable()
    {
        var root = NewRoot();
        try
        {
            await using var provider = await BuildProviderAsync(root);
            var purchasing = provider.GetRequiredService<IPurchasingService>();
            var product = await CreateProductAsync(provider, "RETURN", false);
            var supplier = await CreateSupplierAsync(purchasing, "Return Supplier");

            var receipt = await purchasing.PostGoodsReceiptAsync(new GoodsReceiptPostRequest(
                Guid.NewGuid().ToString(), supplier.Id, null, null, DateTimeOffset.UtcNow,
                0m, 0m, null, null, null, null,
                [new GoodsReceiptLineRequest(null, product.ProductUnitId, 10m, 10m)],
                [new GoodsReceiptExpenseRequest("transport", 20m)]));

            Assert.Equal(120m, receipt.Header.NetTotal);
            Assert.Equal(12m, receipt.Items[0].BaseUnitLandedCost);

            var returned = await purchasing.PostPurchaseReturnAsync(new PurchaseReturnRequest(
                Guid.NewGuid().ToString(), receipt.Header.Id, "Damaged on delivery",
                [new PurchaseReturnLineRequest(receipt.Items[0].Id, 4m)]));

            Assert.Equal(48m, returned.ReturnTotal);
            Assert.Equal(72m, returned.ReceiptBalanceDue);
            Assert.Equal(72m, returned.SupplierBalanceAfter);

            var factory = provider.GetRequiredService<IDbContextFactory<PosDbContext>>();
            await using var context = await factory.CreateDbContextAsync();
            var productDb = await context.Products.AsNoTracking().SingleAsync(x => x.Id == product.ProductId);
            var lineDb = await context.GoodsReceiptItems.AsNoTracking().SingleAsync(x => x.Id == receipt.Items[0].Id);
            var layer = await context.InventoryCostLayers.AsNoTracking().SingleAsync(x => x.Id == lineDb.InventoryCostLayerId);
            Assert.Equal(6m, productDb.StockOnHand);
            Assert.Equal(6m, layer.RemainingQuantityBase);

            var returnMovement = await context.StockMovements.AsNoTracking()
                .SingleAsync(x => x.MovementType == "purchase_return");
            Assert.Equal(-4m, returnMovement.QuantityBase);
            Assert.Equal(12m, returnMovement.UnitCostBase);
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public async Task Purchase_return_rejects_stock_consumed_by_sales_and_rolls_back()
    {
        var root = NewRoot();
        try
        {
            await using var provider = await BuildProviderAsync(root);
            var purchasing = provider.GetRequiredService<IPurchasingService>();
            var pos = provider.GetRequiredService<IPosService>();
            var product = await CreateProductAsync(provider, "CONSUMED", false, 20m);
            var supplier = await CreateSupplierAsync(purchasing, "Consumed Supplier");
            var receipt = await DirectReceiptAsync(purchasing, supplier.Id, product.ProductUnitId, 10m, 10m, 0m, null);

            await pos.OpenShiftAsync(1000m);
            await pos.CheckoutAsync(new PosCheckoutRequest(
                Guid.NewGuid().ToString(),
                [new PosCheckoutLineRequest(product.ProductUnitId, 6m)],
                0m,
                [new PosPaymentRequest("cash", 120m, 120m)]));

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                purchasing.PostPurchaseReturnAsync(new PurchaseReturnRequest(
                    Guid.NewGuid().ToString(), receipt.Header.Id, "Too much after sales",
                    [new PurchaseReturnLineRequest(receipt.Items[0].Id, 5m)])));
            Assert.Contains("consumed", error.Message, StringComparison.OrdinalIgnoreCase);

            var factory = provider.GetRequiredService<IDbContextFactory<PosDbContext>>();
            await using var context = await factory.CreateDbContextAsync();
            Assert.Empty(await context.PurchaseReturns.ToListAsync());
            Assert.Empty(await context.StockMovements.Where(x => x.MovementType == "purchase_return").ToListAsync());
            Assert.Equal(4m, (await context.Products.AsNoTracking().SingleAsync(x => x.Id == product.ProductId)).StockOnHand);
            Assert.Equal(100m, (await context.Suppliers.AsNoTracking().SingleAsync(x => x.Id == supplier.Id)).CurrentBalance);
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public async Task Fully_paid_purchase_return_creates_supplier_credit()
    {
        var root = NewRoot();
        try
        {
            await using var provider = await BuildProviderAsync(root);
            var purchasing = provider.GetRequiredService<IPurchasingService>();
            var pos = provider.GetRequiredService<IPosService>();
            var product = await CreateProductAsync(provider, "CREDIT", false);
            var supplier = await CreateSupplierAsync(purchasing, "Credit Supplier");
            await pos.OpenShiftAsync(1000m);

            var receipt = await DirectReceiptAsync(purchasing, supplier.Id, product.ProductUnitId, 10m, 10m, 100m, "cash");
            Assert.Equal(0m, (await purchasing.GetSupplierAsync(supplier.Id))!.Supplier.CurrentBalance);

            var result = await purchasing.PostPurchaseReturnAsync(new PurchaseReturnRequest(
                Guid.NewGuid().ToString(), receipt.Header.Id, "Paid stock returned",
                [new PurchaseReturnLineRequest(receipt.Items[0].Id, 2m)]));

            Assert.Equal(20m, result.ReturnTotal);
            Assert.Equal(0m, result.ReceiptBalanceDue);
            Assert.Equal(-20m, result.SupplierBalanceAfter);

            var detail = await purchasing.GetSupplierAsync(supplier.Id);
            Assert.Equal(-20m, detail!.Supplier.CurrentBalance);
            Assert.Contains(detail.Ledger, x =>
                x.EntryType == "purchase_return" && x.Debit == 20m && x.BalanceAfter == -20m);
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public async Task Expiry_purchase_return_uses_original_receipt_batch()
    {
        var root = NewRoot();
        try
        {
            await using var provider = await BuildProviderAsync(root);
            var purchasing = provider.GetRequiredService<IPurchasingService>();
            var product = await CreateProductAsync(provider, "EXP-RETURN", true);
            var supplier = await CreateSupplierAsync(purchasing, "Expiry Supplier");

            var receipt = await purchasing.PostGoodsReceiptAsync(new GoodsReceiptPostRequest(
                Guid.NewGuid().ToString(), supplier.Id, null, null, DateTimeOffset.UtcNow,
                0m, 0m, null, null, null, null,
                [new GoodsReceiptLineRequest(
                    null, product.ProductUnitId, 10m, 10m, 0m,
                    "BATCH-RET", DateTime.Today, DateTime.Today.AddYears(1))],
                []));

            await purchasing.PostPurchaseReturnAsync(new PurchaseReturnRequest(
                Guid.NewGuid().ToString(), receipt.Header.Id, "Supplier recall",
                [new PurchaseReturnLineRequest(receipt.Items[0].Id, 3m)]));

            var factory = provider.GetRequiredService<IDbContextFactory<PosDbContext>>();
            await using var context = await factory.CreateDbContextAsync();
            var batch = await context.ProductBatches.AsNoTracking()
                .SingleAsync(x => x.ProductId == product.ProductId && x.BatchNumber == "BATCH-RET");
            Assert.Equal(7m, batch.StockOnHand);

            var movement = await context.StockMovements.AsNoTracking()
                .SingleAsync(x => x.MovementType == "purchase_return");
            Assert.Equal(batch.Id, movement.ProductBatchId);
            Assert.Equal(-3m, movement.QuantityBase);
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public async Task Receipt_return_and_supplier_payment_retries_are_idempotent_and_overpayment_rolls_back()
    {
        var root = NewRoot();
        try
        {
            await using var provider = await BuildProviderAsync(root);
            var purchasing = provider.GetRequiredService<IPurchasingService>();
            var pos = provider.GetRequiredService<IPosService>();
            var product = await CreateProductAsync(provider, "IDEM", false);
            var supplier = await CreateSupplierAsync(purchasing, "Idempotent Supplier");
            await pos.OpenShiftAsync(1000m);

            var receiptKey = Guid.NewGuid().ToString();
            var request = new GoodsReceiptPostRequest(
                receiptKey, supplier.Id, null, "IDEM-INV", DateTimeOffset.UtcNow,
                0m, 0m, null, null, null, null,
                [new GoodsReceiptLineRequest(null, product.ProductUnitId, 10m, 10m)], []);
            var first = await purchasing.PostGoodsReceiptAsync(request);
            var second = await purchasing.PostGoodsReceiptAsync(request);
            Assert.Equal(first.Header.Id, second.Header.Id);

            var returnKey = Guid.NewGuid().ToString();
            var returnRequest = new PurchaseReturnRequest(
                returnKey, first.Header.Id, "Retry safe",
                [new PurchaseReturnLineRequest(first.Items[0].Id, 4m)]);
            var ret1 = await purchasing.PostPurchaseReturnAsync(returnRequest);
            var ret2 = await purchasing.PostPurchaseReturnAsync(returnRequest);
            Assert.Equal(ret1.Id, ret2.Id);

            var overpay = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                purchasing.RecordSupplierPaymentAsync(new SupplierPaymentRequest(
                    Guid.NewGuid().ToString(), supplier.Id, 61m, "cash", null, null, null)));
            Assert.Contains("cannot exceed", overpay.Message, StringComparison.OrdinalIgnoreCase);

            var factory = provider.GetRequiredService<IDbContextFactory<PosDbContext>>();
            await using var context = await factory.CreateDbContextAsync();
            Assert.Single(await context.GoodsReceipts.ToListAsync());
            Assert.Single(await context.PurchaseReturns.ToListAsync());
            Assert.Empty(await context.SupplierPayments.ToListAsync());
            Assert.Equal(60m, (await context.Suppliers.AsNoTracking().SingleAsync(x => x.Id == supplier.Id)).CurrentBalance);
        }
        finally { Cleanup(root); }
    }

    private static async Task<ServiceProvider> BuildProviderAsync(string root)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IApplicationPaths>(new TestPaths(root));
        services.AddBusinessOSPosPersistence();
        var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<ILocalDatabaseInitializer>().InitializeAsync();
        await provider.GetRequiredService<IOwnerBootstrapService>()
            .CreateOwnerAsync("Batch 08 Owner", "owner", "Password-123", "en");
        await provider.GetRequiredService<IUserSessionService>().LoginAsync("owner", "Password-123");
        return provider;
    }

    private static async Task<(long ProductId, long ProductUnitId)> CreateProductAsync(
        ServiceProvider provider,
        string sku,
        bool trackExpiry,
        decimal sellingPrice = 20m)
    {
        var catalog = provider.GetRequiredService<IProductCatalogService>();
        var refs = await catalog.GetReferenceDataAsync();
        var pcs = refs.Units.First(x => x.Code == "PCS");
        var shortId = Guid.NewGuid().ToString("N").Substring(0, 6).ToUpperInvariant();
        var saved = await catalog.SaveProductAsync(new CatalogProductSaveRequest(
            null, sku + "-" + shortId,
            "Batch 08 " + sku, null, null, null, null, pcs.Id,
            null, null, null, null, 10m, sellingPrice, null, null,
            0m, 0m, true, trackExpiry, true, [], []));
        return (saved.Id, saved.Units.Single(x => x.UnitId == pcs.Id).Id);
    }

    private static Task<SupplierSummary> CreateSupplierAsync(IPurchasingService purchasing, string name) =>
        purchasing.SaveSupplierAsync(new SupplierSaveRequest(
            null, name, null, "0700000000", null, "Kabul", 0m, null, true));

    private static Task<GoodsReceiptDetail> DirectReceiptAsync(
        IPurchasingService purchasing,
        long supplierId,
        long productUnitId,
        decimal quantity,
        decimal unitCost,
        decimal paidAmount,
        string? method) =>
        purchasing.PostGoodsReceiptAsync(new GoodsReceiptPostRequest(
            Guid.NewGuid().ToString(), supplierId, null, null, DateTimeOffset.UtcNow,
            0m, paidAmount, method, null, null, null,
            [new GoodsReceiptLineRequest(null, productUnitId, quantity, unitCost)], []));

    private static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "BusinessOS-POS-batch08-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void Cleanup(string root)
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    private sealed class TestPaths(string root) : IApplicationPaths
    {
        public string RootPath { get; } = root;
        public string DatabasePath { get; } = Path.Combine(root, "batch08.db");
        public void EnsureCreated() => Directory.CreateDirectory(RootPath);
    }
}
