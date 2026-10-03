using BusinessOS.POS.Application.Abstractions.Authentication;
using BusinessOS.POS.Application.Abstractions.Cash;
using BusinessOS.POS.Application.Abstractions.Catalog;
using BusinessOS.POS.Application.Abstractions.Customers;
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

public sealed class CashDrawerIntegrationTests
{
    [Fact]
    public async Task Shift_opening_creates_one_opening_float_and_is_idempotent()
    {
        var root = NewRoot();
        try
        {
            await using var provider = await BuildProviderAsync(root);
            var cash = provider.GetRequiredService<ICashManagementService>();
            var terminal = (await cash.GetTerminalsAsync()).Single(x => x.Code == "COUNTER-1");
            var key = Guid.NewGuid().ToString();
            var request = new ShiftOpenRequest(key, terminal.Id, 500m);

            var first = await cash.OpenShiftAsync(request);
            var second = await cash.OpenShiftAsync(request);

            Assert.Equal(first.Shift.Id, second.Shift.Id);
            Assert.Equal(500m, first.Shift.ExpectedCash);
            var opening = Assert.Single(first.Movements);
            Assert.Equal("opening_float", opening.MovementType);
            Assert.Equal(500m, opening.Amount);
            Assert.Equal(500m, opening.ExpectedCashAfter);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                cash.OpenShiftAsync(request with { OpeningCash = 600m }));
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public async Task Zero_opening_float_still_creates_opening_movement()
    {
        var root = NewRoot();
        try
        {
            await using var provider = await BuildProviderAsync(root);
            var cash = provider.GetRequiredService<ICashManagementService>();
            var terminal = (await cash.GetTerminalsAsync()).Single();
            var shift = await cash.OpenShiftAsync(new ShiftOpenRequest(
                Guid.NewGuid().ToString(), terminal.Id, 0m));

            var movement = Assert.Single(shift.Movements);
            Assert.Equal("opening_float", movement.MovementType);
            Assert.Equal(0m, movement.Amount);
            Assert.Equal(0m, shift.Shift.ExpectedCash);
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public async Task Cash_drawer_reconciles_all_cash_sources_and_manual_movements()
    {
        var root = NewRoot();
        try
        {
            await using var provider = await BuildProviderAsync(root);
            var cash = provider.GetRequiredService<ICashManagementService>();
            var pos = provider.GetRequiredService<IPosService>();
            var customers = provider.GetRequiredService<ICustomerService>();
            var purchasing = provider.GetRequiredService<IPurchasingService>();
            var returns = provider.GetRequiredService<ISaleReturnService>();

            var terminal = (await cash.GetTerminalsAsync()).Single();
            var shift = await cash.OpenShiftAsync(new ShiftOpenRequest(
                Guid.NewGuid().ToString(), terminal.Id, 1000m));

            var product = await CreateProductAsync(provider, "CASH-FLOW", 30m);
            var supplier = await purchasing.SaveSupplierAsync(new SupplierSaveRequest(
                null, "Cash Drawer Supplier", null, "0700000088", null, "Kabul", 0m, null, true));

            await purchasing.PostGoodsReceiptAsync(new GoodsReceiptPostRequest(
                Guid.NewGuid().ToString(), supplier.Id, null, null, DateTimeOffset.UtcNow,
                0m, 20m, "cash", null, null, null,
                [new GoodsReceiptLineRequest(null, product.ProductUnitId, 10m, 10m)], []));

            var cashSale = await pos.CheckoutAsync(new PosCheckoutRequest(
                Guid.NewGuid().ToString(),
                [new PosCheckoutLineRequest(product.ProductUnitId, 2m)],
                0m,
                [new PosPaymentRequest("cash", 60m, 60m)]));

            var customer = await customers.SaveCustomerAsync(new CustomerSaveRequest(
                null, "Drawer Customer", "0700000011", null, "Kabul",
                200m, 0m, true));

            var creditSale = await pos.CheckoutAsync(new PosCheckoutRequest(
                Guid.NewGuid().ToString(),
                [new PosCheckoutLineRequest(product.ProductUnitId, 1m)],
                0m, [], CustomerId: customer.Id));
            Assert.Equal(30m, creditSale.BalanceDue);

            await customers.CollectAsync(new CustomerCollectionRequest(
                Guid.NewGuid().ToString(), customer.Id, "cash", 30m, 30m,
                null, null, null));

            await purchasing.RecordSupplierPaymentAsync(new SupplierPaymentRequest(
                Guid.NewGuid().ToString(), supplier.Id, 40m, "cash",
                "SP-40", null, null));

            var categories = await cash.GetExpenseCategoriesAsync();
            var methods = await cash.GetPaymentMethodsAsync();
            var cashMethod = methods.Single(x => x.Code == "cash");
            var rent = categories.Single(x => x.Code == "rent");
            var otherIncome = categories.Single(x => x.Code == "other_income");

            await cash.RecordOperatingEntryAsync(new OperatingEntryRequest(
                Guid.NewGuid().ToString(), "expense", rent.Id, cashMethod.Id,
                25m, null, "Cash expense test", null));

            await cash.RecordOperatingEntryAsync(new OperatingEntryRequest(
                Guid.NewGuid().ToString(), "income", otherIncome.Id, cashMethod.Id,
                10m, null, "Cash other income test", null));

            var factory = provider.GetRequiredService<IDbContextFactory<PosDbContext>>();
            long saleItemId;
            await using (var context = await factory.CreateDbContextAsync())
            {
                saleItemId = await context.SaleItems
                    .Where(x => x.SaleId == cashSale.SaleId)
                    .Select(x => x.Id)
                    .SingleAsync();
            }

            await returns.ReturnAsync(new SaleReturnRequest(
                Guid.NewGuid().ToString(), cashSale.SaleId, "Refund one unit",
                [new SaleReturnLineRequest(saleItemId, 1m)],
                [new SaleRefundRequest("cash", 30m)]));

            var depositKey = Guid.NewGuid().ToString();
            var firstDeposit = await cash.RecordManualMovementAsync(new ManualCashMovementRequest(
                depositKey, shift.Shift.Id, "cash_deposit", 100m, "Top up drawer"));
            var retryDeposit = await cash.RecordManualMovementAsync(new ManualCashMovementRequest(
                depositKey, shift.Shift.Id, "cash_deposit", 100m, "Top up drawer"));
            Assert.Equal(firstDeposit.Id, retryDeposit.Id);

            await cash.RecordManualMovementAsync(new ManualCashMovementRequest(
                Guid.NewGuid().ToString(), shift.Shift.Id, "cash_withdrawal",
                20m, "Petty cash withdrawal"));
            await cash.RecordManualMovementAsync(new ManualCashMovementRequest(
                Guid.NewGuid().ToString(), shift.Shift.Id, "drawer_to_safe",
                15m, "Move excess cash to safe"));

            var current = await cash.GetCurrentShiftAsync();
            Assert.NotNull(current);
            Assert.Equal(1050m, current!.Shift.ExpectedCash);
            Assert.Equal(11, current.Movements.Count);

            Assert.Single(current.Movements.Where(x => x.MovementType == "purchase_payment"));
            Assert.Single(current.Movements.Where(x => x.MovementType == "cash_sale"));
            Assert.Single(current.Movements.Where(x => x.MovementType == "customer_collection"));
            Assert.Single(current.Movements.Where(x => x.MovementType == "supplier_payment"));
            Assert.Single(current.Movements.Where(x => x.MovementType == "expense"));
            Assert.Single(current.Movements.Where(x => x.MovementType == "other_income"));
            Assert.Single(current.Movements.Where(x => x.MovementType == "sale_refund"));
            Assert.Single(current.Movements.Where(x => x.MovementType == "cash_deposit"));
            Assert.Single(current.Movements.Where(x => x.MovementType == "cash_withdrawal"));
            Assert.Single(current.Movements.Where(x => x.MovementType == "drawer_to_safe"));

            var supplierDetail = await purchasing.GetSupplierAsync(supplier.Id);
            Assert.Equal(40m, supplierDetail!.Supplier.CurrentBalance);
            var customerDetail = await customers.GetCustomerAsync(customer.Id);
            Assert.Equal(0m, customerDetail!.Customer.CurrentBalance);

            await using var verify = await factory.CreateDbContextAsync();
            var sale = await verify.Sales.AsNoTracking().SingleAsync(x => x.Id == cashSale.SaleId);
            Assert.Equal(30m, sale.RefundedTotal);
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public async Task Non_cash_transactions_do_not_require_or_change_cash_drawer()
    {
        var root = NewRoot();
        try
        {
            await using var provider = await BuildProviderAsync(root);
            var cash = provider.GetRequiredService<ICashManagementService>();
            var pos = provider.GetRequiredService<IPosService>();
            var purchasing = provider.GetRequiredService<IPurchasingService>();
            var product = await CreateProductAsync(provider, "NON-CASH", 30m);
            var supplier = await purchasing.SaveSupplierAsync(new SupplierSaveRequest(
                null, "Non Cash Supplier", null, null, null, null, 0m, null, true));

            await purchasing.PostGoodsReceiptAsync(new GoodsReceiptPostRequest(
                Guid.NewGuid().ToString(), supplier.Id, null, null, DateTimeOffset.UtcNow,
                0m, 0m, null, null, null, null,
                [new GoodsReceiptLineRequest(null, product.ProductUnitId, 10m, 10m)], []));

            var sale = await pos.CheckoutAsync(new PosCheckoutRequest(
                Guid.NewGuid().ToString(),
                [new PosCheckoutLineRequest(product.ProductUnitId, 1m)],
                0m,
                [new PosPaymentRequest("bank", 30m, 30m)]));
            Assert.Equal(0m, sale.BalanceDue);

            var categories = await cash.GetExpenseCategoriesAsync();
            var methods = await cash.GetPaymentMethodsAsync();
            await cash.RecordOperatingEntryAsync(new OperatingEntryRequest(
                Guid.NewGuid().ToString(), "expense",
                categories.Single(x => x.Code == "internet").Id,
                methods.Single(x => x.Code == "bank").Id,
                12m, null, "Bank-paid internet", null));

            var factory = provider.GetRequiredService<IDbContextFactory<PosDbContext>>();
            await using var context = await factory.CreateDbContextAsync();
            Assert.Empty(await context.CashierShifts.ToListAsync());
            Assert.Empty(await context.CashMovements.ToListAsync());
            Assert.Single(await context.OperatingEntries.ToListAsync());
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public async Task Cash_operating_entry_without_shift_rolls_back()
    {
        var root = NewRoot();
        try
        {
            await using var provider = await BuildProviderAsync(root);
            var cash = provider.GetRequiredService<ICashManagementService>();
            var categories = await cash.GetExpenseCategoriesAsync();
            var methods = await cash.GetPaymentMethodsAsync();

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                cash.RecordOperatingEntryAsync(new OperatingEntryRequest(
                    Guid.NewGuid().ToString(), "expense",
                    categories.Single(x => x.Code == "repair").Id,
                    methods.Single(x => x.Code == "cash").Id,
                    20m, null, "Requires drawer", null)));
            Assert.Contains("Open a cashier shift", error.Message);

            var factory = provider.GetRequiredService<IDbContextFactory<PosDbContext>>();
            await using var context = await factory.CreateDbContextAsync();
            Assert.Empty(await context.OperatingEntries.ToListAsync());
            Assert.Empty(await context.CashMovements.ToListAsync());
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public async Task Shift_close_requires_variance_reason_is_idempotent_and_can_reopen()
    {
        var root = NewRoot();
        try
        {
            await using var provider = await BuildProviderAsync(root);
            var cash = provider.GetRequiredService<ICashManagementService>();
            var terminal = (await cash.GetTerminalsAsync()).Single();
            var shift = await cash.OpenShiftAsync(new ShiftOpenRequest(
                Guid.NewGuid().ToString(), terminal.Id, 500m));
            await cash.RecordManualMovementAsync(new ManualCashMovementRequest(
                Guid.NewGuid().ToString(), shift.Shift.Id, "cash_deposit", 100m, "Top up"));

            var closeKey = Guid.NewGuid().ToString();
            var noReason = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                cash.CloseShiftAsync(new ShiftCloseRequest(
                    closeKey, shift.Shift.Id, 590m, null, "Closing")));
            Assert.Contains("variance reason", noReason.Message, StringComparison.OrdinalIgnoreCase);

            var request = new ShiftCloseRequest(
                closeKey, shift.Shift.Id, 590m, "AFN 10 short", "Closing");
            var closed = await cash.CloseShiftAsync(request);
            var retry = await cash.CloseShiftAsync(request);
            Assert.Equal(closed.ClosureId, retry.ClosureId);
            Assert.Equal(600m, closed.ExpectedCash);
            Assert.Equal(-10m, closed.Variance);
            Assert.False(closed.WithinTolerance);

            var selected = await cash.GetShiftAsync(shift.Shift.Id);
            Assert.Equal("closed", selected!.Shift.Status);
            Assert.Single(selected.Closures);

            await cash.ReopenShiftAsync(shift.Shift.Id, "Recount required");
            var reopened = await cash.GetCurrentShiftAsync();
            Assert.NotNull(reopened);
            Assert.Equal("open", reopened!.Shift.Status);
            Assert.Equal(600m, reopened.Shift.ExpectedCash);
            Assert.Equal(2, reopened.Movements.Count);
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public async Task Manual_cash_movement_retry_conflict_is_rejected()
    {
        var root = NewRoot();
        try
        {
            await using var provider = await BuildProviderAsync(root);
            var cash = provider.GetRequiredService<ICashManagementService>();
            var terminal = (await cash.GetTerminalsAsync()).Single();
            var shift = await cash.OpenShiftAsync(new ShiftOpenRequest(
                Guid.NewGuid().ToString(), terminal.Id, 100m));
            var key = Guid.NewGuid().ToString();

            await cash.RecordManualMovementAsync(new ManualCashMovementRequest(
                key, shift.Shift.Id, "cash_deposit", 50m, "Deposit"));

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                cash.RecordManualMovementAsync(new ManualCashMovementRequest(
                    key, shift.Shift.Id, "cash_deposit", 60m, "Deposit")));

            var current = await cash.GetCurrentShiftAsync();
            Assert.Equal(150m, current!.Shift.ExpectedCash);
            Assert.Equal(2, current.Movements.Count);
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
            .CreateOwnerAsync("Cash Owner", "owner", "Password-123", "en");
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
            "Cash Drawer Product", null, null, null, null, pcs.Id,
            null, null, null, null, 10m, sellingPrice, 25m, null,
            0m, 0m, true, false, true, [], []));
        return (saved.Id, saved.Units.Single(x => x.UnitId == pcs.Id).Id);
    }

    private static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "BusinessOS-POS-cash-tests", Guid.NewGuid().ToString("N"));
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
        public string DatabasePath { get; } = Path.Combine(root, "cash.db");
        public void EnsureCreated() => Directory.CreateDirectory(RootPath);
    }
}
