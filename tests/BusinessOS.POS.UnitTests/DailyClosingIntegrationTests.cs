using BusinessOS.POS.Application.Abstractions.Authentication;
using BusinessOS.POS.Application.Abstractions.Cash;
using BusinessOS.POS.Application.Abstractions.Catalog;
using BusinessOS.POS.Application.Abstractions.Closing;
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

public sealed class DailyClosingIntegrationTests
{
    [Fact]
    public async Task Daily_close_rejects_open_shift_then_snapshots_financial_and_cash_totals()
    {
        var root = NewRoot();
        try
        {
            await using var provider = await BuildProviderAsync(root);
            var cash = provider.GetRequiredService<ICashManagementService>();
            var closing = provider.GetRequiredService<IBusinessDayClosingService>();
            var pos = provider.GetRequiredService<IPosService>();
            var purchasing = provider.GetRequiredService<IPurchasingService>();

            var terminal = (await cash.GetTerminalsAsync()).Single();
            var shift = await cash.OpenShiftAsync(new ShiftOpenRequest(
                Guid.NewGuid().ToString(), terminal.Id, 1000m));
            var product = await CreateProductAsync(provider, "DAY-CLOSE", 30m);
            var supplier = await purchasing.SaveSupplierAsync(new SupplierSaveRequest(
                null, "Closing Test Supplier", null, "0700000066", null, "Kabul",
                0m, null, true));

            var receipt = await purchasing.PostGoodsReceiptAsync(new GoodsReceiptPostRequest(
                Guid.NewGuid().ToString(), supplier.Id, null, null, DateTimeOffset.UtcNow,
                0m, 0m, null, null, null, null,
                [new GoodsReceiptLineRequest(null, product.ProductUnitId, 10m, 10m)], []));

            var sale = await pos.CheckoutAsync(new PosCheckoutRequest(
                Guid.NewGuid().ToString(),
                [new PosCheckoutLineRequest(product.ProductUnitId, 2m)],
                0m,
                [new PosPaymentRequest("cash", 60m, 60m)]));

            var categories = await cash.GetExpenseCategoriesAsync();
            var methods = await cash.GetPaymentMethodsAsync();
            await cash.RecordOperatingEntryAsync(new OperatingEntryRequest(
                Guid.NewGuid().ToString(), "expense",
                categories.Single(x => x.Code == "rent").Id,
                methods.Single(x => x.Code == "cash").Id,
                25m, null, "Daily close expense", null));

            var date = shift.Shift.BusinessDate;
            var openShiftError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                closing.CloseAsync(new BusinessDayCloseRequest(
                    Guid.NewGuid().ToString(), date, "Should not close with open shift")));
            Assert.Contains("shifts", openShiftError.Message, StringComparison.OrdinalIgnoreCase);

            var shiftClosure = await cash.CloseShiftAsync(new ShiftCloseRequest(
                Guid.NewGuid().ToString(), shift.Shift.Id, 1030m,
                "AFN 5 cash shortage", null));
            Assert.Equal(1035m, shiftClosure.ExpectedCash);
            Assert.Equal(-5m, shiftClosure.Variance);

            var key = Guid.NewGuid().ToString();
            var closed = await closing.CloseAsync(new BusinessDayCloseRequest(
                key, date, "First daily close"));
            var retry = await closing.CloseAsync(new BusinessDayCloseRequest(
                key, date, "First daily close"));

            Assert.Equal(closed.Id, retry.Id);
            Assert.Equal(1, closed.Version);
            Assert.Equal(1, closed.ShiftCount);
            Assert.Equal(1, closed.SalesCount);
            Assert.Equal(60m, closed.SalesNetTotal);
            Assert.Equal(0m, closed.SalesReturnTotal);
            Assert.Equal(60m, closed.NetSalesTotal);
            Assert.Equal(20m, closed.SalesCogsTotal);
            Assert.Equal(20m, closed.NetCogsTotal);
            Assert.Equal(40m, closed.GrossProfitTotal);
            Assert.Equal(25m, closed.OperatingExpensesTotal);
            Assert.Equal(15m, closed.NetProfitTotal);
            Assert.Equal(100m, closed.PurchasesTotal);
            Assert.Equal(1000m, closed.OpeningCashTotal);
            Assert.Equal(60m, closed.CashInflowTotal);
            Assert.Equal(25m, closed.CashOutflowTotal);
            Assert.Equal(1035m, closed.ExpectedCashTotal);
            Assert.Equal(1030m, closed.ActualCashTotal);
            Assert.Equal(-5m, closed.VarianceTotal);

            var summary = await closing.GetSummaryAsync(date);
            Assert.Equal("closed", summary.Status);
            Assert.Equal(1035m, summary.LedgerExpectedCashTotal);

            var factory = provider.GetRequiredService<IDbContextFactory<PosDbContext>>();
            await using var verify = await factory.CreateDbContextAsync();
            Assert.Equal(60m, (await verify.Sales.AsNoTracking().SingleAsync(x => x.Id == sale.SaleId)).NetTotal);
            Assert.Equal(100m, (await verify.GoodsReceipts.AsNoTracking().SingleAsync(x => x.Id == receipt.Header.Id)).NetTotal);
            Assert.Equal(8m, (await verify.Products.AsNoTracking().SingleAsync(x => x.Id == product.ProductId)).StockOnHand);
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public async Task Closed_business_day_blocks_new_posting_until_reopened_then_reclose_creates_revision_two()
    {
        var root = NewRoot();
        try
        {
            await using var provider = await BuildProviderAsync(root);
            var cash = provider.GetRequiredService<ICashManagementService>();
            var closing = provider.GetRequiredService<IBusinessDayClosingService>();
            var pos = provider.GetRequiredService<IPosService>();
            var purchasing = provider.GetRequiredService<IPurchasingService>();

            var terminal = (await cash.GetTerminalsAsync()).Single();
            var shift = await cash.OpenShiftAsync(new ShiftOpenRequest(
                Guid.NewGuid().ToString(), terminal.Id, 1000m));
            var product = await CreateProductAsync(provider, "DAY-LOCK", 30m);
            var supplier = await purchasing.SaveSupplierAsync(new SupplierSaveRequest(
                null, "Day Lock Supplier", null, null, null, null, 0m, null, true));

            await purchasing.PostGoodsReceiptAsync(new GoodsReceiptPostRequest(
                Guid.NewGuid().ToString(), supplier.Id, null, null, DateTimeOffset.UtcNow,
                0m, 0m, null, null, null, null,
                [new GoodsReceiptLineRequest(null, product.ProductUnitId, 10m, 10m)], []));

            await cash.CloseShiftAsync(new ShiftCloseRequest(
                Guid.NewGuid().ToString(), shift.Shift.Id, 1000m, null, null));

            var date = shift.Shift.BusinessDate;
            var firstClose = await closing.CloseAsync(new BusinessDayCloseRequest(
                Guid.NewGuid().ToString(), date, "Initial close"));
            Assert.Equal(1, firstClose.Version);

            var saleError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                pos.CheckoutAsync(new PosCheckoutRequest(
                    Guid.NewGuid().ToString(),
                    [new PosCheckoutLineRequest(product.ProductUnitId, 1m)],
                    0m,
                    [new PosPaymentRequest("bank", 30m, 30m)])));
            Assert.Contains("closed", saleError.Message, StringComparison.OrdinalIgnoreCase);

            var receiptError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                purchasing.PostGoodsReceiptAsync(new GoodsReceiptPostRequest(
                    Guid.NewGuid().ToString(), supplier.Id, null, null, DateTimeOffset.UtcNow,
                    0m, 0m, null, null, null, null,
                    [new GoodsReceiptLineRequest(null, product.ProductUnitId, 1m, 10m)], [])));
            Assert.Contains("closed", receiptError.Message, StringComparison.OrdinalIgnoreCase);

            var shiftReopenError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                cash.ReopenShiftAsync(shift.Shift.Id, "Cannot reopen while day closed"));
            Assert.Contains("closed", shiftReopenError.Message, StringComparison.OrdinalIgnoreCase);

            var reopenedDay = await closing.ReopenAsync(date, "Post late approved activity");
            Assert.Equal("open", reopenedDay.Status);

            await cash.ReopenShiftAsync(shift.Shift.Id, "Reopen drawer for approved late activity");
            await cash.RecordManualMovementAsync(new ManualCashMovementRequest(
                Guid.NewGuid().ToString(), shift.Shift.Id,
                "cash_deposit", 10m, "Approved late cash correction"));

            var secondShiftClose = await cash.CloseShiftAsync(new ShiftCloseRequest(
                Guid.NewGuid().ToString(), shift.Shift.Id, 1010m, null, null));
            Assert.Equal(2, secondShiftClose.Version);
            Assert.Equal(1010m, secondShiftClose.ExpectedCash);
            Assert.Equal(0m, secondShiftClose.Variance);

            var secondClose = await closing.CloseAsync(new BusinessDayCloseRequest(
                Guid.NewGuid().ToString(), date, "Re-close after approved late activity"));
            Assert.Equal(2, secondClose.Version);
            Assert.Equal(1010m, secondClose.ExpectedCashTotal);
            Assert.Equal(1010m, secondClose.ActualCashTotal);
            Assert.Equal(0m, secondClose.VarianceTotal);

            var revisions = await closing.GetClosuresAsync(date);
            Assert.Equal(2, revisions.Count);
            var revisionOne = revisions.Single(x => x.Version == 1);
            Assert.Equal(1000m, revisionOne.ExpectedCashTotal);
            Assert.Equal(1000m, revisionOne.ActualCashTotal);
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public async Task Daily_close_rejects_cash_ledger_reconciliation_mismatch()
    {
        var root = NewRoot();
        try
        {
            await using var provider = await BuildProviderAsync(root);
            var cash = provider.GetRequiredService<ICashManagementService>();
            var closing = provider.GetRequiredService<IBusinessDayClosingService>();
            var terminal = (await cash.GetTerminalsAsync()).Single();
            var shift = await cash.OpenShiftAsync(new ShiftOpenRequest(
                Guid.NewGuid().ToString(), terminal.Id, 500m));
            await cash.CloseShiftAsync(new ShiftCloseRequest(
                Guid.NewGuid().ToString(), shift.Shift.Id, 500m, null, null));

            var factory = provider.GetRequiredService<IDbContextFactory<PosDbContext>>();
            await using (var context = await factory.CreateDbContextAsync())
            {
                var row = await context.CashierShifts.SingleAsync(x => x.Id == shift.Shift.Id);
                row.ExpectedCash = 501m;
                await context.SaveChangesAsync();
            }

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                closing.CloseAsync(new BusinessDayCloseRequest(
                    Guid.NewGuid().ToString(), shift.Shift.BusinessDate, "Mismatch test")));
            Assert.Contains("cash movement ledger", error.Message, StringComparison.OrdinalIgnoreCase);

            Assert.Empty(await closing.GetClosuresAsync(shift.Shift.BusinessDate));
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public async Task Daily_close_idempotency_key_rejects_changed_notes()
    {
        var root = NewRoot();
        try
        {
            await using var provider = await BuildProviderAsync(root);
            var cash = provider.GetRequiredService<ICashManagementService>();
            var closing = provider.GetRequiredService<IBusinessDayClosingService>();
            var terminal = (await cash.GetTerminalsAsync()).Single();
            var shift = await cash.OpenShiftAsync(new ShiftOpenRequest(
                Guid.NewGuid().ToString(), terminal.Id, 0m));
            await cash.CloseShiftAsync(new ShiftCloseRequest(
                Guid.NewGuid().ToString(), shift.Shift.Id, 0m, null, null));

            var key = Guid.NewGuid().ToString();
            var first = await closing.CloseAsync(new BusinessDayCloseRequest(
                key, shift.Shift.BusinessDate, "Original notes"));
            var retry = await closing.CloseAsync(new BusinessDayCloseRequest(
                key, shift.Shift.BusinessDate, "Original notes"));
            Assert.Equal(first.Id, retry.Id);

            var conflict = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                closing.CloseAsync(new BusinessDayCloseRequest(
                    key, shift.Shift.BusinessDate, "Changed notes")));
            Assert.Contains("another closing payload", conflict.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public async Task Reopen_requires_reason_and_preserves_prior_snapshot()
    {
        var root = NewRoot();
        try
        {
            await using var provider = await BuildProviderAsync(root);
            var cash = provider.GetRequiredService<ICashManagementService>();
            var closing = provider.GetRequiredService<IBusinessDayClosingService>();
            var terminal = (await cash.GetTerminalsAsync()).Single();
            var shift = await cash.OpenShiftAsync(new ShiftOpenRequest(
                Guid.NewGuid().ToString(), terminal.Id, 200m));
            await cash.CloseShiftAsync(new ShiftCloseRequest(
                Guid.NewGuid().ToString(), shift.Shift.Id, 200m, null, null));

            var first = await closing.CloseAsync(new BusinessDayCloseRequest(
                Guid.NewGuid().ToString(), shift.Shift.BusinessDate, "Snapshot one"));

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                closing.ReopenAsync(shift.Shift.BusinessDate, "   "));

            await closing.ReopenAsync(shift.Shift.BusinessDate, "Approved correction");
            var revisions = await closing.GetClosuresAsync(shift.Shift.BusinessDate);
            var preserved = Assert.Single(revisions);
            Assert.Equal(first.Id, preserved.Id);
            Assert.Equal("Snapshot one", preserved.Notes);
            Assert.Equal(200m, preserved.ExpectedCashTotal);
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
            .CreateOwnerAsync("Closing Owner", "owner", "Password-123", "en");
        await provider.GetRequiredService<IUserSessionService>()
            .LoginAsync("owner", "Password-123");
        return provider;
    }

    private static async Task<(long ProductId, long ProductUnitId)> CreateProductAsync(
        ServiceProvider provider, string sku, decimal sellingPrice)
    {
        var catalog = provider.GetRequiredService<IProductCatalogService>();
        var refs = await catalog.GetReferenceDataAsync();
        var pcs = refs.Units.First(x => x.Code == "PCS");
        var saved = await catalog.SaveProductAsync(new CatalogProductSaveRequest(
            null, sku + "-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(),
            "Closing Test Product", null, null, null, null, pcs.Id,
            null, null, null, null, 10m, sellingPrice, 25m, null,
            0m, 0m, true, false, true, [], []));
        return (saved.Id, saved.Units.Single(x => x.UnitId == pcs.Id).Id);
    }

    private static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "BusinessOS-POS-closing-tests", Guid.NewGuid().ToString("N"));
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
        public string DatabasePath { get; } = Path.Combine(root, "closing.db");
        public void EnsureCreated() => Directory.CreateDirectory(RootPath);
    }
}
