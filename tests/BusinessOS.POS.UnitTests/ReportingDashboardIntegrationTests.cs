using BusinessOS.POS.Application.Abstractions.Authentication;
using BusinessOS.POS.Application.Abstractions.Cash;
using BusinessOS.POS.Application.Abstractions.Catalog;
using BusinessOS.POS.Application.Abstractions.Closing;
using BusinessOS.POS.Application.Abstractions.Dashboard;
using BusinessOS.POS.Application.Abstractions.Persistence;
using BusinessOS.POS.Application.Abstractions.Purchasing;
using BusinessOS.POS.Application.Abstractions.Reporting;
using BusinessOS.POS.Application.Abstractions.Sales;
using BusinessOS.POS.Application.Abstractions.Storage;
using BusinessOS.POS.Persistence;
using BusinessOS.POS.Persistence.Entities;
using BusinessOS.POS.Persistence.Security;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BusinessOS.POS.UnitTests;

public sealed class ReportingDashboardIntegrationTests
{
    [Fact]
    public async Task Report_reconciles_returns_profit_and_inventory_value_from_historical_cost()
    {
        var root = NewRoot();
        try
        {
            await using var provider = await BuildProviderAsync(root);
            var catalog = provider.GetRequiredService<IProductCatalogService>();
            var purchasing = provider.GetRequiredService<IPurchasingService>();
            var pos = provider.GetRequiredService<IPosService>();
            var returns = provider.GetRequiredService<ISaleReturnService>();
            var cash = provider.GetRequiredService<ICashManagementService>();
            var reports = provider.GetRequiredService<IReportingService>();

            var category = await catalog.SaveCategoryAsync(new CatalogCategorySaveRequest(
                null, null, "Beverages", "نوشیدنی", "څښاک", 10, true));
            var product = await CreateProductAsync(provider, "REPORT-HISTORY", 30m, 2m, category.Id);
            var supplier = await purchasing.SaveSupplierAsync(new SupplierSaveRequest(
                null, "Reporting Supplier", null, "0700000055", null, "Kabul", 0m, null, true));

            await purchasing.PostGoodsReceiptAsync(new GoodsReceiptPostRequest(
                Guid.NewGuid().ToString(), supplier.Id, null, null, DateTimeOffset.UtcNow,
                0m, 0m, null, null, null, null,
                [new GoodsReceiptLineRequest(null, product.ProductUnitId, 10m, 10m)], []));

            var sale = await pos.CheckoutAsync(new PosCheckoutRequest(
                Guid.NewGuid().ToString(),
                [new PosCheckoutLineRequest(product.ProductUnitId, 2m)],
                0m,
                [new PosPaymentRequest("bank", 60m, 60m)]));

            var factory = provider.GetRequiredService<IDbContextFactory<PosDbContext>>();
            long saleItemId;
            await using (var context = await factory.CreateDbContextAsync())
            {
                saleItemId = await context.SaleItems
                    .Where(x => x.SaleId == sale.SaleId)
                    .Select(x => x.Id)
                    .SingleAsync();
            }

            await returns.ReturnAsync(new SaleReturnRequest(
                Guid.NewGuid().ToString(), sale.SaleId, "One unit returned",
                [new SaleReturnLineRequest(saleItemId, 1m)],
                [new SaleRefundRequest("bank", 30m, "BANK-REFUND-1")]));

            var categories = await cash.GetExpenseCategoriesAsync();
            var methods = await cash.GetPaymentMethodsAsync();
            var bank = methods.Single(x => x.Code == "bank");

            await cash.RecordOperatingEntryAsync(new OperatingEntryRequest(
                Guid.NewGuid().ToString(), "expense",
                categories.Single(x => x.Code == "rent").Id,
                bank.Id, 5m, null, "Reporting expense", null));
            await cash.RecordOperatingEntryAsync(new OperatingEntryRequest(
                Guid.NewGuid().ToString(), "income",
                categories.Single(x => x.Code == "other_income").Id,
                bank.Id, 2m, null, "Reporting income", null));

            await using (var context = await factory.CreateDbContextAsync())
            {
                var row = await context.Products.SingleAsync(x => x.Id == product.ProductId);
                row.PurchaseCost = 99m;
                await context.SaveChangesAsync();
            }

            var report = await reports.BuildAsync(new ReportFilters(DateTime.Today, DateTime.Today));

            Assert.True(report.CanViewProfit);
            Assert.Equal(1, report.Summary.SalesCount);
            Assert.Equal(60m, report.Summary.SalesNet);
            Assert.Equal(30m, report.Summary.Returns);
            Assert.Equal(30m, report.Summary.NetSales);
            Assert.Equal(20m, report.Summary.SalesCogs);
            Assert.Equal(10m, report.Summary.CogsReversed);
            Assert.Equal(10m, report.Summary.NetCogs);
            Assert.Equal(20m, report.Summary.GrossProfit);
            Assert.Equal(5m, report.Summary.Expenses);
            Assert.Equal(2m, report.Summary.OtherIncome);
            Assert.Equal(17m, report.Summary.NetProfit);
            Assert.Equal(30m, report.Summary.AverageOrderValue);
            Assert.Equal(90m, report.Summary.InventoryValue);

            var top = Assert.Single(report.TopProducts);
            Assert.Equal(product.ProductId, top.ProductId);
            Assert.Equal(1m, top.QuantityBase);
            Assert.Equal(30m, top.NetSales);
            Assert.Equal(10m, top.Cogs);
            Assert.Equal(20m, top.GrossProfit);

            var categoryRow = Assert.Single(report.CategoryProfit);
            Assert.Equal(category.Id, categoryRow.CategoryId);
            Assert.Equal(30m, categoryRow.NetSales);
            Assert.Equal(10m, categoryRow.Cogs);
            Assert.Equal(20m, categoryRow.GrossProfit);

            var trend = Assert.Single(report.SalesTrend);
            Assert.Equal(DateTime.Today, trend.BusinessDate);
            Assert.Equal(30m, trend.NetSales);
            Assert.Equal(20m, trend.GrossProfit);
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public async Task Report_filters_and_sales_export_use_same_range_and_lookup_scope()
    {
        var root = NewRoot();
        try
        {
            await using var provider = await BuildProviderAsync(root);
            var catalog = provider.GetRequiredService<IProductCatalogService>();
            var purchasing = provider.GetRequiredService<IPurchasingService>();
            var pos = provider.GetRequiredService<IPosService>();
            var reports = provider.GetRequiredService<IReportingService>();

            var category = await catalog.SaveCategoryAsync(new CatalogCategorySaveRequest(
                null, null, "Filtered Category", "دسته فیلتر", "فلټر کټګوري", 20, true));
            var product = await CreateProductAsync(provider, "REPORT-FILTER", 30m, 2m, category.Id);
            var supplier = await purchasing.SaveSupplierAsync(new SupplierSaveRequest(
                null, "Filter Supplier", null, null, null, null, 0m, null, true));

            await purchasing.PostGoodsReceiptAsync(new GoodsReceiptPostRequest(
                Guid.NewGuid().ToString(), supplier.Id, null, null, DateTimeOffset.UtcNow,
                0m, 0m, null, null, null, null,
                [new GoodsReceiptLineRequest(null, product.ProductUnitId, 5m, 10m)], []));

            await pos.CheckoutAsync(new PosCheckoutRequest(
                Guid.NewGuid().ToString(),
                [new PosCheckoutLineRequest(product.ProductUnitId, 1m)],
                0m,
                [new PosPaymentRequest("bank", 30m, 30m)]));

            var filters = new ReportFilters(
                DateTime.Today, DateTime.Today,
                SupplierId: supplier.Id,
                ProductId: product.ProductId,
                CategoryId: category.Id);

            var report = await reports.BuildAsync(filters);
            var csv = await reports.GetSalesExportAsync(filters);
            var lookups = await reports.GetLookupsAsync();

            Assert.Equal(1, report.Summary.SalesCount);
            Assert.Equal(30m, report.Summary.NetSales);
            Assert.Equal(50m, report.Summary.Purchases);
            var export = Assert.Single(csv);
            Assert.Equal(30m, export.NetTotal);
            Assert.Contains(lookups.Products, x => x.Id == product.ProductId && x.Code.StartsWith("REPORT-FILTER"));
            Assert.Contains(lookups.Categories, x => x.Id == category.Id);
            Assert.Contains(lookups.Suppliers, x => x.Id == supplier.Id);
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public async Task Dashboard_exposes_live_sales_cash_payables_and_low_stock()
    {
        var root = NewRoot();
        try
        {
            await using var provider = await BuildProviderAsync(root);
            var purchasing = provider.GetRequiredService<IPurchasingService>();
            var pos = provider.GetRequiredService<IPosService>();
            var cash = provider.GetRequiredService<ICashManagementService>();
            var dashboard = provider.GetRequiredService<IDashboardService>();

            var product = await CreateProductAsync(provider, "DASH-LIVE", 30m, 5m, null);
            var supplier = await purchasing.SaveSupplierAsync(new SupplierSaveRequest(
                null, "Dashboard Supplier", null, null, null, null, 0m, null, true));

            await purchasing.PostGoodsReceiptAsync(new GoodsReceiptPostRequest(
                Guid.NewGuid().ToString(), supplier.Id, null, null, DateTimeOffset.UtcNow,
                0m, 0m, null, null, null, null,
                [new GoodsReceiptLineRequest(null, product.ProductUnitId, 3m, 10m)], []));

            var terminal = (await cash.GetTerminalsAsync()).Single();
            await cash.OpenShiftAsync(new ShiftOpenRequest(
                Guid.NewGuid().ToString(), terminal.Id, 100m));

            await pos.CheckoutAsync(new PosCheckoutRequest(
                Guid.NewGuid().ToString(),
                [new PosCheckoutLineRequest(product.ProductUnitId, 1m)],
                0m,
                [new PosPaymentRequest("bank", 30m, 30m)]));

            var result = await dashboard.GetAsync();

            Assert.Equal(30m, result.TodayNetSales);
            Assert.Equal(1, result.TodayTransactions);
            Assert.Equal(30m, result.Payables);
            Assert.Equal(0m, result.Receivables);
            Assert.NotNull(result.OpenShift);
            Assert.Equal(100m, result.OpenShift!.ExpectedCash);
            Assert.True(result.LowStockCount >= 1);
            Assert.Contains(result.LowStockProducts, x => x.ProductId == product.ProductId && x.StockOnHand == 2m);
            Assert.Contains(result.RecentSales, x => x.NetTotal == 30m);
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public async Task Report_viewer_without_profit_permission_receives_no_profit_fields()
    {
        var root = NewRoot();
        try
        {
            await using var provider = await BuildProviderAsync(root);
            var purchasing = provider.GetRequiredService<IPurchasingService>();
            var pos = provider.GetRequiredService<IPosService>();
            var product = await CreateProductAsync(provider, "REPORT-SECURE", 30m, 0m, null);
            var supplier = await purchasing.SaveSupplierAsync(new SupplierSaveRequest(
                null, "Permission Supplier", null, null, null, null, 0m, null, true));

            await purchasing.PostGoodsReceiptAsync(new GoodsReceiptPostRequest(
                Guid.NewGuid().ToString(), supplier.Id, null, null, DateTimeOffset.UtcNow,
                0m, 0m, null, null, null, null,
                [new GoodsReceiptLineRequest(null, product.ProductUnitId, 2m, 10m)], []));
            await pos.CheckoutAsync(new PosCheckoutRequest(
                Guid.NewGuid().ToString(),
                [new PosCheckoutLineRequest(product.ProductUnitId, 1m)],
                0m,
                [new PosPaymentRequest("bank", 30m, 30m)]));

            await CreateReportViewerAsync(provider);

            var sessions = provider.GetRequiredService<IUserSessionService>();
            await sessions.LogoutAsync();
            await sessions.LoginAsync("reporter", "Password-123");

            var reports = provider.GetRequiredService<IReportingService>();
            var result = await reports.BuildAsync(new ReportFilters(DateTime.Today, DateTime.Today));
            var export = Assert.Single(await reports.GetSalesExportAsync(
                new ReportFilters(DateTime.Today, DateTime.Today)));

            Assert.False(result.CanViewProfit);
            Assert.Null(result.Summary.SalesCogs);
            Assert.Null(result.Summary.GrossProfit);
            Assert.Null(result.Summary.NetProfit);
            Assert.Null(result.Summary.InventoryValue);
            Assert.All(result.TopProducts, x =>
            {
                Assert.Null(x.Cogs);
                Assert.Null(x.GrossProfit);
            });
            Assert.Null(export.CogsTotal);
            Assert.Null(export.GrossProfit);
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public async Task Report_includes_daily_closing_history_peak_hour_and_weekday()
    {
        var root = NewRoot();
        try
        {
            await using var provider = await BuildProviderAsync(root);
            var purchasing = provider.GetRequiredService<IPurchasingService>();
            var pos = provider.GetRequiredService<IPosService>();
            var cash = provider.GetRequiredService<ICashManagementService>();
            var closing = provider.GetRequiredService<IBusinessDayClosingService>();
            var reports = provider.GetRequiredService<IReportingService>();

            var product = await CreateProductAsync(provider, "REPORT-TIME", 30m, 0m, null);
            var supplier = await purchasing.SaveSupplierAsync(new SupplierSaveRequest(
                null, "Time Supplier", null, null, null, null, 0m, null, true));
            await purchasing.PostGoodsReceiptAsync(new GoodsReceiptPostRequest(
                Guid.NewGuid().ToString(), supplier.Id, null, null, DateTimeOffset.UtcNow,
                0m, 0m, null, null, null, null,
                [new GoodsReceiptLineRequest(null, product.ProductUnitId, 2m, 10m)], []));

            var terminal = (await cash.GetTerminalsAsync()).Single();
            var shift = await cash.OpenShiftAsync(new ShiftOpenRequest(
                Guid.NewGuid().ToString(), terminal.Id, 0m));
            await pos.CheckoutAsync(new PosCheckoutRequest(
                Guid.NewGuid().ToString(),
                [new PosCheckoutLineRequest(product.ProductUnitId, 1m)],
                0m,
                [new PosPaymentRequest("bank", 30m, 30m)]));
            await cash.CloseShiftAsync(new ShiftCloseRequest(
                Guid.NewGuid().ToString(), shift.Shift.Id, 0m, null, null));
            var dayClose = await closing.CloseAsync(new BusinessDayCloseRequest(
                Guid.NewGuid().ToString(), shift.Shift.BusinessDate, "Reporting close"));

            var result = await reports.BuildAsync(new ReportFilters(DateTime.Today, DateTime.Today));

            Assert.Contains(result.ClosingHistory, x => x.ClosureId == dayClose.Id && x.Version == 1);
            Assert.Contains(result.PeakHours, x => x.SalesCount == 1 && x.NetSales == 30m);
            Assert.Contains(result.Weekdays, x => x.SalesCount == 1 && x.NetSales == 30m);
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
            .CreateOwnerAsync("Reporting Owner", "owner", "Password-123", "en");
        await provider.GetRequiredService<IUserSessionService>()
            .LoginAsync("owner", "Password-123");
        return provider;
    }

    private static async Task<(long ProductId, long ProductUnitId)> CreateProductAsync(
        ServiceProvider provider,
        string skuPrefix,
        decimal sellingPrice,
        decimal minimumStock,
        long? categoryId)
    {
        var catalog = provider.GetRequiredService<IProductCatalogService>();
        var refs = await catalog.GetReferenceDataAsync();
        var pcs = refs.Units.First(x => x.Code == "PCS");
        var saved = await catalog.SaveProductAsync(new CatalogProductSaveRequest(
            null,
            skuPrefix + "-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(),
            "Reporting Product",
            "محصول گزارش",
            "د راپور محصول",
            categoryId,
            null,
            pcs.Id,
            null, null, null, null,
            10m,
            sellingPrice,
            25m,
            null,
            minimumStock,
            5m,
            true,
            false,
            true,
            [],
            []));
        return (saved.Id, saved.Units.Single(x => x.UnitId == pcs.Id).Id);
    }

    private static async Task CreateReportViewerAsync(ServiceProvider provider)
    {
        var factory = provider.GetRequiredService<IDbContextFactory<PosDbContext>>();
        var hasher = provider.GetRequiredService<PasswordHasher>();
        await using var context = await factory.CreateDbContextAsync();

        var permission = await context.Permissions.SingleAsync(x => x.Name == "reports.view");
        var role = new RoleEntity
        {
            Name = "report_viewer",
            Label = "Report Viewer",
            IsSystem = false,
        };
        role.Permissions.Add(permission);

        var now = DateTimeOffset.UtcNow;
        var user = new UserEntity
        {
            Name = "Report Viewer",
            Username = "reporter",
            NormalizedUsername = "reporter",
            PasswordHash = hasher.Hash("Password-123"),
            PreferredLocale = "en",
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
        user.Roles.Add(role);
        context.Users.Add(user);
        await context.SaveChangesAsync();
    }

    private static string NewRoot()
    {
        var root = Path.Combine(
            Path.GetTempPath(), "BusinessOS-POS-report-tests", Guid.NewGuid().ToString("N"));
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
        public string DatabasePath { get; } = Path.Combine(root, "reports.db");
        public void EnsureCreated() => Directory.CreateDirectory(RootPath);
    }
}
