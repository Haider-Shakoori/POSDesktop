using BusinessOS.POS.Application.Abstractions.Authentication;
using BusinessOS.POS.Application.Abstractions.Backup;
using BusinessOS.POS.Application.Abstractions.Cash;
using BusinessOS.POS.Application.Abstractions.Catalog;
using BusinessOS.POS.Application.Abstractions.Closing;
using BusinessOS.POS.Application.Abstractions.Customers;
using BusinessOS.POS.Application.Abstractions.Dashboard;
using BusinessOS.POS.Application.Abstractions.Persistence;
using BusinessOS.POS.Application.Abstractions.Purchasing;
using BusinessOS.POS.Application.Abstractions.Reporting;
using BusinessOS.POS.Application.Abstractions.Sales;
using BusinessOS.POS.Application.Abstractions.Storage;
using BusinessOS.POS.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BusinessOS.POS.UnitTests;

public sealed class FinalGoldenPathUatTests
{
    [Fact]
    public async Task Full_business_day_golden_path_reconciles_stock_cash_accounts_reports_and_backup()
    {
        var root = NewRoot();
        try
        {
            await using var provider = await BuildProviderAsync(root);
            var catalog = provider.GetRequiredService<IProductCatalogService>();
            var purchasing = provider.GetRequiredService<IPurchasingService>();
            var customers = provider.GetRequiredService<ICustomerService>();
            var cash = provider.GetRequiredService<ICashManagementService>();
            var pos = provider.GetRequiredService<IPosService>();
            var returns = provider.GetRequiredService<ISaleReturnService>();
            var closing = provider.GetRequiredService<IBusinessDayClosingService>();
            var reports = provider.GetRequiredService<IReportingService>();
            var dashboard = provider.GetRequiredService<IDashboardService>();
            var backups = provider.GetRequiredService<ILocalBackupService>();

            var refs = await catalog.GetReferenceDataAsync();
            var pcs = refs.Units.Single(x => x.Code == "PCS");
            var product = await catalog.SaveProductAsync(new CatalogProductSaveRequest(
                null, "UAT-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(),
                "Golden Path Product", "محصول مسیر طلایی", "د زرینې لارې محصول",
                null, null, pcs.Id,
                null, null, null, null,
                10m, 30m, 25m, null,
                2m, 5m, true, false, true, [], []));
            var productUnitId = product.Units.Single(x => x.UnitId == pcs.Id).Id;

            var supplier = await purchasing.SaveSupplierAsync(new SupplierSaveRequest(
                null, "Golden Supplier", null, "0700000201", null, "Kabul",
                0m, "UAT supplier", true));
            var receipt = await purchasing.PostGoodsReceiptAsync(new GoodsReceiptPostRequest(
                Guid.NewGuid().ToString(), supplier.Id, null, "UAT-INV",
                DateTimeOffset.UtcNow, 0m, 20m, "bank", "BANK-20", null, "UAT receipt",
                [new GoodsReceiptLineRequest(null, productUnitId, 10m, 10m)], []));
            Assert.Equal(100m, receipt.Header.NetTotal);
            Assert.Equal(80m, receipt.Header.BalanceDue);

            var supplierPayment = await purchasing.RecordSupplierPaymentAsync(new SupplierPaymentRequest(
                Guid.NewGuid().ToString(), supplier.Id, 30m, "bank",
                "BANK-SP-30", null, "UAT supplier payment"));
            Assert.Equal(50m, supplierPayment.SupplierBalanceAfter);

            var customer = await customers.SaveCustomerAsync(new CustomerSaveRequest(
                null, "Golden Customer", "0700000202", null, "Kabul",
                500m, 60m, true));

            var terminal = (await cash.GetTerminalsAsync()).Single(x => x.IsActive);
            var shift = await cash.OpenShiftAsync(new ShiftOpenRequest(
                Guid.NewGuid().ToString(), terminal.Id, 500m));

            var sale = await pos.CheckoutAsync(new PosCheckoutRequest(
                Guid.NewGuid().ToString(),
                [new PosCheckoutLineRequest(productUnitId, 2m)],
                0m,
                [new PosPaymentRequest("cash", 60m, 60m)]));
            Assert.Equal(60m, sale.NetTotal);
            Assert.Equal(20m, sale.CogsTotal);

            var factory = provider.GetRequiredService<IDbContextFactory<PosDbContext>>();
            long saleItemId;
            await using (var context = await factory.CreateDbContextAsync())
            {
                saleItemId = await context.SaleItems
                    .Where(x => x.SaleId == sale.SaleId)
                    .Select(x => x.Id)
                    .SingleAsync();
            }

            var saleReturn = await returns.ReturnAsync(new SaleReturnRequest(
                Guid.NewGuid().ToString(), sale.SaleId, "UAT customer return",
                [new SaleReturnLineRequest(saleItemId, 1m)],
                [new SaleRefundRequest("cash", 30m)]));
            Assert.Equal(30m, saleReturn.ReturnTotal);
            Assert.Equal(10m, saleReturn.CogsReversed);
            Assert.Equal(30m, saleReturn.RefundTotal);

            var collection = await customers.CollectAsync(new CustomerCollectionRequest(
                Guid.NewGuid().ToString(), customer.Id, "cash", 30m, 30m,
                "UAT-COLLECT", null, "UAT opening-balance collection"));
            Assert.Equal(30m, collection.CustomerBalanceAfter);

            var categories = await cash.GetExpenseCategoriesAsync();
            var methods = await cash.GetPaymentMethodsAsync();
            var cashMethod = methods.Single(x => x.Code == "cash");
            var bankMethod = methods.Single(x => x.Code == "bank");

            await cash.RecordOperatingEntryAsync(new OperatingEntryRequest(
                Guid.NewGuid().ToString(), "expense",
                categories.Single(x => x.Code == "rent").Id,
                cashMethod.Id, 20m, null, "UAT rent", null));
            await cash.RecordOperatingEntryAsync(new OperatingEntryRequest(
                Guid.NewGuid().ToString(), "income",
                categories.Single(x => x.Code == "other_income").Id,
                bankMethod.Id, 5m, null, "UAT other income", null));

            var currentShift = await cash.GetCurrentShiftAsync();
            Assert.NotNull(currentShift);
            Assert.Equal(540m, currentShift!.Shift.ExpectedCash);

            var shiftClose = await cash.CloseShiftAsync(new ShiftCloseRequest(
                Guid.NewGuid().ToString(), shift.Shift.Id, 540m, null, "UAT exact close"));
            Assert.Equal(0m, shiftClose.Variance);

            var dayClose = await closing.CloseAsync(new BusinessDayCloseRequest(
                Guid.NewGuid().ToString(), shift.Shift.BusinessDate, "Final golden-path UAT"));
            Assert.Equal(1, dayClose.SalesCount);
            Assert.Equal(30m, dayClose.NetSalesTotal);
            Assert.Equal(10m, dayClose.NetCogsTotal);
            Assert.Equal(20m, dayClose.GrossProfitTotal);
            Assert.Equal(30m, dayClose.CustomerCollectionsTotal);
            Assert.Equal(100m, dayClose.PurchasesTotal);
            Assert.Equal(50m, dayClose.SupplierPaymentsTotal);
            Assert.Equal(20m, dayClose.OperatingExpensesTotal);
            Assert.Equal(5m, dayClose.OtherIncomeTotal);
            Assert.Equal(5m, dayClose.NetProfitTotal);
            Assert.Equal(500m, dayClose.OpeningCashTotal);
            Assert.Equal(90m, dayClose.CashInflowTotal);
            Assert.Equal(50m, dayClose.CashOutflowTotal);
            Assert.Equal(540m, dayClose.ExpectedCashTotal);
            Assert.Equal(540m, dayClose.ActualCashTotal);
            Assert.Equal(0m, dayClose.VarianceTotal);

            var report = await reports.BuildAsync(new ReportFilters(
                shift.Shift.BusinessDate, shift.Shift.BusinessDate));
            Assert.Equal(30m, report.Summary.NetSales);
            Assert.Equal(20m, report.Summary.GrossProfit);
            Assert.Equal(5m, report.Summary.NetProfit);
            Assert.Equal(100m, report.Summary.Purchases);
            Assert.Equal(30m, report.Summary.Collections);
            Assert.Equal(50m, report.Summary.SupplierPayments);

            var live = await dashboard.GetAsync();
            Assert.Equal(30m, live.TodayNetSales);
            Assert.Null(live.OpenShift);
            Assert.Equal(30m, live.Receivables);
            Assert.Equal(50m, live.Payables);
            Assert.Contains(live.RecentSales, x => x.SaleId == sale.SaleId);

            await using (var context = await factory.CreateDbContextAsync())
            {
                var dbProduct = await context.Products.AsNoTracking()
                    .SingleAsync(x => x.Id == product.Id);
                Assert.Equal(9m, dbProduct.StockOnHand);

                var remaining = await context.InventoryCostLayers.AsNoTracking()
                    .Where(x => x.ProductId == product.Id)
                    .SumAsync(x => x.RemainingQuantityBase);
                Assert.Equal(9m, remaining);
            }

            var backup = await backups.CreateAsync("uat");
            Assert.True(backup.IsVerified);
            Assert.True(File.Exists(backup.FullPath));
            var verification = await backups.VerifyAsync(backup.FullPath);
            Assert.True(verification.IsValid);
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
            .CreateOwnerAsync("Golden Path Owner", "owner", "Password-123", "en");
        await provider.GetRequiredService<IUserSessionService>()
            .LoginAsync("owner", "Password-123");
        return provider;
    }

    private static string NewRoot()
    {
        var root = Path.Combine(
            Path.GetTempPath(), "BusinessOS-POS-final-uat", Guid.NewGuid().ToString("N"));
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
        public string DatabasePath { get; } = Path.Combine(root, "uat.db");
        public string BackupsDirectory { get; } = Path.Combine(root, "Backups");
        public string UpdatesDirectory { get; } = Path.Combine(root, "Updates");
        public void EnsureCreated()
        {
            Directory.CreateDirectory(RootPath);
            Directory.CreateDirectory(BackupsDirectory);
            Directory.CreateDirectory(UpdatesDirectory);
        }
    }
}
