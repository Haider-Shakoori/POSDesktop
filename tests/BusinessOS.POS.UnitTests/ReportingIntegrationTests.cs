using BusinessOS.POS.Application.Abstractions.Authentication;
using BusinessOS.POS.Application.Abstractions.Cash;
using BusinessOS.POS.Application.Abstractions.Catalog;
using BusinessOS.POS.Application.Abstractions.Persistence;
using BusinessOS.POS.Application.Abstractions.Purchasing;
using BusinessOS.POS.Application.Abstractions.Reports;
using BusinessOS.POS.Application.Abstractions.Sales;
using BusinessOS.POS.Application.Abstractions.Storage;
using BusinessOS.POS.Persistence;
using BusinessOS.POS.Persistence.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BusinessOS.POS.UnitTests;

public sealed class ReportingIntegrationTests
{
    [Fact]
    public async Task Report_reconciles_returns_profit_and_inventory_value_from_historical_fifo_cost()
    {
        var root = NewRoot();
        try
        {
            await using var provider = await BuildProviderAsync(root);
            var factory = provider.GetRequiredService<IDbContextFactory<PosDbContext>>();
            long categoryId;
            await using (var context = await factory.CreateDbContextAsync())
            {
                var categoryEntity = new CategoryEntity
                {
                    NameEn = "Beverages",
                    NameFa = "نوشیدنی",
                    NamePs = "څښاک",
                    SortOrder = 10,
                    IsActive = true,
                };
                context.Categories.Add(categoryEntity);
                await context.SaveChangesAsync();
                categoryId = categoryEntity.Id;
            }

            var product = await CreateProductAsync(provider, "REPORT", 30m, categoryId, 2m);
            var purchasing = provider.GetRequiredService<IPurchasingService>();
            var supplier = await purchasing.SaveSupplierAsync(new SupplierSaveRequest(
                null, "Reporting Supplier", null, "0700000055", null, "Kabul",
                0m, null, true));

            await purchasing.PostGoodsReceiptAsync(new GoodsReceiptPostRequest(
                Guid.NewGuid().ToString(), supplier.Id, null, null, DateTimeOffset.UtcNow,
                0m, 0m, null, null, null, null,
                [new GoodsReceiptLineRequest(null, product.ProductUnitId, 10m, 10m)], []));

            var pos = provider.GetRequiredService<IPosService>();
            var sale = await pos.CheckoutAsync(new PosCheckoutRequest(
                Guid.NewGuid().ToString(),
                [new PosCheckoutLineRequest(product.ProductUnitId, 2m)],
                0m,
                [new PosPaymentRequest("bank", 60m, 60m)]));

            long saleItemId;
            await using (var context = await factory.CreateDbContextAsync())
            {
                saleItemId = await context.SaleItems
                    .Where(x => x.SaleId == sale.SaleId)
                    .Select(x => x.Id)
                    .SingleAsync();
            }

            await provider.GetRequiredService<ISaleReturnService>().ReturnAsync(
                new SaleReturnRequest(
                    Guid.NewGuid().ToString(), sale.SaleId, "One unit returned",
                    [new SaleReturnLineRequest(saleItemId, 1m)],
                    [new SaleRefundRequest("bank", 30m)]));

            var cash = provider.GetRequiredService<ICashManagementService>();
            var categories = await cash.GetExpenseCategoriesAsync();
            var methods = await cash.GetPaymentMethodsAsync();
            var bank = methods.Single(x => x.Code == "bank");
            await cash.RecordOperatingEntryAsync(new OperatingEntryRequest(
                Guid.NewGuid().ToString(), "expense",
                categories.Single(x => x.Code == "rent").Id, bank.Id,
                5m, null, "Reporting expense", null));
            await cash.RecordOperatingEntryAsync(new OperatingEntryRequest(
                Guid.NewGuid().ToString(), "income",
                categories.Single(x => x.Code == "other_income").Id, bank.Id,
                2m, null, "Reporting income", null));

            await using (var context = await factory.CreateDbContextAsync())
            {
                var p = await context.Products.SingleAsync(x => x.Id == product.ProductId);
                p.PurchaseCost = 99m;
                await context.SaveChangesAsync();
            }

            var report = await provider.GetRequiredService<IReportingService>().BuildAsync(
                new ReportFilters(DateTime.Today, DateTime.Today));

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
            Assert.Equal(30m, report.Summary.Aov);
            Assert.Equal(90m, report.Summary.InventoryValue);

            var top = Assert.Single(report.TopProducts);
            Assert.Equal(product.ProductId, top.ProductId);
            Assert.Equal(1m, top.QuantityBase);
            Assert.Equal(30m, top.NetSales);
            Assert.Equal(10m, top.Cogs);
            Assert.Equal(20m, top.GrossProfit);

            var categoryRow = Assert.Single(report.CategoryProfit);
            Assert.Equal(categoryId, categoryRow.CategoryId);
            Assert.Equal(30m, categoryRow.NetSales);
            Assert.Equal(10m, categoryRow.Cogs);
            Assert.Equal(20m, categoryRow.GrossProfit);

            var trend = Assert.Single(report.SalesTrend);
            Assert.Equal(DateTime.Today, trend.Day);
            Assert.Equal(30m, trend.NetSales);
            Assert.Equal(20m, trend.GrossProfit);
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public async Task Report_filters_scope_sales_balances_and_supplier_activity()
    {
        var root = NewRoot();
        try
        {
            await using var provider = await BuildProviderAsync(root);
            var factory = provider.GetRequiredService<IDbContextFactory<PosDbContext>>();
            long categoryId;
            long selectedCustomerId;
            await using (var context = await factory.CreateDbContextAsync())
            {
                var category = new CategoryEntity { NameEn = "Filtered", SortOrder = 1, IsActive = true };
                context.Categories.Add(category);
                var selected = new CustomerEntity
                {
                    Name = "Selected Customer", Phone = "0700000101", CreditLimit = 1000m,
                    OpeningBalance = 0m, CurrentBalance = 0m, IsActive = true,
                };
                var other = new CustomerEntity
                {
                    Name = "Other Customer", Phone = "0700000102", CreditLimit = 1000m,
                    OpeningBalance = 75m, CurrentBalance = 75m, IsActive = true,
                };
                context.Customers.AddRange(selected, other);
                await context.SaveChangesAsync();
                categoryId = category.Id;
                selectedCustomerId = selected.Id;
            }

            var product = await CreateProductAsync(provider, "FILTER", 30m, categoryId, 2m);
            var purchasing = provider.GetRequiredService<IPurchasingService>();
            var supplier = await purchasing.SaveSupplierAsync(new SupplierSaveRequest(
                null, "Selected Supplier", null, null, null, null, 0m, null, true));
            await purchasing.PostGoodsReceiptAsync(new GoodsReceiptPostRequest(
                Guid.NewGuid().ToString(), supplier.Id, null, null, DateTimeOffset.UtcNow,
                0m, 0m, null, null, null, null,
                [new GoodsReceiptLineRequest(null, product.ProductUnitId, 10m, 10m)], []));

            await using (var context = await factory.CreateDbContextAsync())
            {
                context.Suppliers.Add(new SupplierEntity
                {
                    Name = "Other Supplier", OpeningBalance = 125m,
                    CurrentBalance = 125m, IsActive = true,
                });
                await context.SaveChangesAsync();
            }

            var pos = provider.GetRequiredService<IPosService>();
            await pos.CheckoutAsync(new PosCheckoutRequest(
                Guid.NewGuid().ToString(),
                [new PosCheckoutLineRequest(product.ProductUnitId, 1m)],
                0m, [new PosPaymentRequest("bank", 30m, 30m)],
                CustomerId: selectedCustomerId));

            long otherCustomerId;
            await using (var context = await factory.CreateDbContextAsync())
                otherCustomerId = await context.Customers
                    .Where(x => x.Name == "Other Customer").Select(x => x.Id).SingleAsync();

            await pos.CheckoutAsync(new PosCheckoutRequest(
                Guid.NewGuid().ToString(),
                [new PosCheckoutLineRequest(product.ProductUnitId, 2m)],
                0m, [new PosPaymentRequest("bank", 60m, 60m)],
                CustomerId: otherCustomerId));

            var report = await provider.GetRequiredService<IReportingService>().BuildAsync(
                new ReportFilters(DateTime.Today, DateTime.Today,
                    selectedCustomerId, supplier.Id, product.ProductId, categoryId));

            Assert.Equal(30m, report.Summary.NetSales);
            Assert.Equal(0m, report.Summary.Receivables);
            Assert.Equal(100m, report.Summary.Payables);
            Assert.Equal(100m, report.Summary.Purchases);
            Assert.Equal(30m, Assert.Single(report.SalesTrend).NetSales);
            Assert.Equal(30m, Assert.Single(report.TopProducts).NetSales);
            Assert.Equal(30m, Assert.Single(report.CategoryProfit).NetSales);
            Assert.Equal(30m, Assert.Single(report.PeakHours).NetSales);
            Assert.Equal(30m, Assert.Single(report.Weekdays).NetSales);
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public async Task Live_dashboard_surfaces_today_sales_drawer_balances_and_low_stock()
    {
        var root = NewRoot();
        try
        {
            await using var provider = await BuildProviderAsync(root);
            var product = await CreateProductAsync(provider, "DASH", 30m, null, 10m);
            var purchasing = provider.GetRequiredService<IPurchasingService>();
            var supplier = await purchasing.SaveSupplierAsync(new SupplierSaveRequest(
                null, "Dashboard Supplier", null, null, null, null, 50m, null, true));
            await purchasing.PostGoodsReceiptAsync(new GoodsReceiptPostRequest(
                Guid.NewGuid().ToString(), supplier.Id, null, null, DateTimeOffset.UtcNow,
                0m, 0m, null, null, null, null,
                [new GoodsReceiptLineRequest(null, product.ProductUnitId, 5m, 10m)], []));

            var factory = provider.GetRequiredService<IDbContextFactory<PosDbContext>>();
            await using (var context = await factory.CreateDbContextAsync())
            {
                context.Customers.Add(new CustomerEntity
                {
                    Name = "Dashboard Customer", OpeningBalance = 40m,
                    CurrentBalance = 40m, CreditLimit = 100m, IsActive = true,
                });
                await context.SaveChangesAsync();
            }

            var cash = provider.GetRequiredService<ICashManagementService>();
            var terminal = (await cash.GetTerminalsAsync()).Single();
            await cash.OpenShiftAsync(new ShiftOpenRequest(
                Guid.NewGuid().ToString(), terminal.Id, 500m));

            await provider.GetRequiredService<IPosService>().CheckoutAsync(new PosCheckoutRequest(
                Guid.NewGuid().ToString(),
                [new PosCheckoutLineRequest(product.ProductUnitId, 1m)],
                0m, [new PosPaymentRequest("cash", 30m, 30m)]));

            var dashboard = await provider.GetRequiredService<IReportingService>().GetDashboardAsync();

            Assert.Equal(30m, dashboard.TodayNetSales);
            Assert.Equal(1, dashboard.TodayTransactions);
            Assert.Equal(40m, dashboard.Receivables);
            Assert.Equal(100m, dashboard.Payables);
            Assert.Equal(1, dashboard.LowStockCount);
            Assert.Equal(530m, dashboard.CurrentShiftExpectedCash);
            Assert.Equal("Main Counter", dashboard.CurrentShiftTerminal);
            Assert.Single(dashboard.RecentSales);
            Assert.Single(dashboard.LowStockProducts);
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
            .CreateOwnerAsync("Report Owner", "owner", "Password-123", "en");
        await provider.GetRequiredService<IUserSessionService>()
            .LoginAsync("owner", "Password-123");
        return provider;
    }

    private static async Task<(long ProductId, long ProductUnitId)> CreateProductAsync(
        ServiceProvider provider, string sku, decimal sellingPrice, long? categoryId, decimal minimumStock)
    {
        var catalog = provider.GetRequiredService<IProductCatalogService>();
        var refs = await catalog.GetReferenceDataAsync();
        var pcs = refs.Units.First(x => x.Code == "PCS");
        var saved = await catalog.SaveProductAsync(new CatalogProductSaveRequest(
            null, sku + "-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(),
            "Reporting Product", null, null, categoryId, null, pcs.Id,
            null, null, null, null, 10m, sellingPrice, 25m, null,
            minimumStock, 5m, true, false, true, [], []));
        return (saved.Id, saved.Units.Single(x => x.UnitId == pcs.Id).Id);
    }

    private static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "BusinessOS-POS-report-tests", Guid.NewGuid().ToString("N"));
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
