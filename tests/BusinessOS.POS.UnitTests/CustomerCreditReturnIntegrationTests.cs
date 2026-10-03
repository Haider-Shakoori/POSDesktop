using BusinessOS.POS.Application.Abstractions.Authentication;
using BusinessOS.POS.Application.Abstractions.Customers;
using BusinessOS.POS.Application.Abstractions.Persistence;
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

public sealed class CustomerCreditReturnIntegrationTests
{
    [Fact]
    public async Task Opening_balance_creates_matching_customer_ledger_entry()
    {
        var root = NewRoot();
        try
        {
            await using var provider = await BuildProviderAsync(root);
            var customers = provider.GetRequiredService<ICustomerService>();

            var customer = await customers.SaveCustomerAsync(new CustomerSaveRequest(
                null, "Opening Balance Customer", "0700123456", null, "Kabul",
                500m, 25m, true));

            Assert.Equal(25m, customer.CurrentBalance);

            var detail = await customers.GetCustomerAsync(customer.Id);
            Assert.NotNull(detail);
            var entry = Assert.Single(detail!.Ledger);
            Assert.Equal("opening_balance", entry.EntryType);
            Assert.Equal(25m, entry.Debit);
            Assert.Equal(0m, entry.Credit);
            Assert.Equal(25m, entry.BalanceAfter);
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public async Task Full_credit_sale_requires_customer_and_posts_receivable()
    {
        var root = NewRoot();
        try
        {
            await using var provider = await BuildProviderAsync(root);
            var customers = provider.GetRequiredService<ICustomerService>();
            var pos = provider.GetRequiredService<IPosService>();

            var customer = await customers.SaveCustomerAsync(new CustomerSaveRequest(
                null, "Credit Customer", "0700000001", null, null,
                1000m, 0m, true));
            var product = (await pos.SearchProductsAsync("6291001000001")).Single();
            var before = product.AvailableQuantity;

            var sale = await pos.CheckoutAsync(new PosCheckoutRequest(
                Guid.NewGuid().ToString(),
                [new PosCheckoutLineRequest(product.ProductUnitId, 1m)],
                0m,
                [],
                "Full customer credit",
                customer.Id));

            Assert.Equal("unpaid", sale.PaymentStatus);
            Assert.Equal(product.Price, sale.BalanceDue);
            Assert.Equal(0m, sale.PaidAmount);
            Assert.Equal(customer.Name, sale.CustomerName);

            var customerAfter = await customers.GetCustomerAsync(customer.Id);
            Assert.NotNull(customerAfter);
            Assert.Equal(product.Price, customerAfter!.Customer.CurrentBalance);
            Assert.Single(customerAfter.OutstandingSales);
            Assert.Equal(product.Price, customerAfter.OutstandingSales[0].BalanceDue);

            var saleDebit = customerAfter.Ledger.Single(x => x.EntryType == "sale");
            Assert.Equal(product.Price, saleDebit.Debit);
            Assert.Equal(0m, saleDebit.Credit);

            var after = (await pos.SearchProductsAsync("6291001000001")).Single().AvailableQuantity;
            Assert.Equal(before - 1m, after);
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public async Task Walk_in_credit_is_rejected_and_stock_rolls_back()
    {
        var root = NewRoot();
        try
        {
            await using var provider = await BuildProviderAsync(root);
            var pos = provider.GetRequiredService<IPosService>();
            var product = (await pos.SearchProductsAsync("6291001000001")).Single();
            var before = product.AvailableQuantity;

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                pos.CheckoutAsync(new PosCheckoutRequest(
                    Guid.NewGuid().ToString(),
                    [new PosCheckoutLineRequest(product.ProductUnitId, 1m)],
                    0m,
                    [])));

            Assert.Contains("registered customer", error.Message);
            var after = (await pos.SearchProductsAsync("6291001000001")).Single().AvailableQuantity;
            Assert.Equal(before, after);

            var factory = provider.GetRequiredService<IDbContextFactory<PosDbContext>>();
            await using var context = await factory.CreateDbContextAsync();
            Assert.Empty(await context.Sales.ToListAsync());
            Assert.Empty(await context.StockMovements.Where(x => x.MovementType == "sale").ToListAsync());
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public async Task Cashier_credit_limit_failure_is_atomic()
    {
        var root = NewRoot();
        try
        {
            await using var provider = await BuildProviderAsync(root);
            var customers = provider.GetRequiredService<ICustomerService>();
            var pos = provider.GetRequiredService<IPosService>();
            var sessions = provider.GetRequiredService<IUserSessionService>();

            var customer = await customers.SaveCustomerAsync(new CustomerSaveRequest(
                null, "Limited Customer", "0700000002", null, null,
                10m, 0m, true));
            var product = (await pos.SearchProductsAsync("6291001000001")).Single();
            var before = product.AvailableQuantity;

            await CreateCashierAsync(provider);
            await sessions.LogoutAsync();
            await sessions.LoginAsync("cashier7", "Password-123");

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                pos.CheckoutAsync(new PosCheckoutRequest(
                    Guid.NewGuid().ToString(),
                    [new PosCheckoutLineRequest(product.ProductUnitId, 1m)],
                    0m,
                    [],
                    null,
                    customer.Id)));

            Assert.Contains("credit limit", error.Message, StringComparison.OrdinalIgnoreCase);

            var factory = provider.GetRequiredService<IDbContextFactory<PosDbContext>>();
            await using var context = await factory.CreateDbContextAsync();
            Assert.Empty(await context.Sales.ToListAsync());
            Assert.Empty(await context.CustomerLedgerEntries.Where(x => x.CustomerId == customer.Id).ToListAsync());

            var customerAfter = await context.Customers.AsNoTracking().SingleAsync(x => x.Id == customer.Id);
            Assert.Equal(0m, customerAfter.CurrentBalance);

            var after = (await pos.SearchProductsAsync("6291001000001")).Single().AvailableQuantity;
            Assert.Equal(before, after);
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public async Task Partial_customer_payment_posts_exact_receivable_and_collection_allocates_oldest_first()
    {
        var root = NewRoot();
        try
        {
            await using var provider = await BuildProviderAsync(root);
            var customers = provider.GetRequiredService<ICustomerService>();
            var pos = provider.GetRequiredService<IPosService>();

            var customer = await customers.SaveCustomerAsync(new CustomerSaveRequest(
                null, "Collection Customer", "0700000003", null, null,
                5000m, 0m, true));
            var product = (await pos.SearchProductsAsync("6291001000001")).Single();
            await pos.OpenShiftAsync(1000m);

            var partialPaid = Money(product.Price / 2m);
            var first = await pos.CheckoutAsync(new PosCheckoutRequest(
                Guid.NewGuid().ToString(),
                [new PosCheckoutLineRequest(product.ProductUnitId, 1m)],
                0m,
                [new PosPaymentRequest("cash", partialPaid, partialPaid)],
                "Partial checkout",
                customer.Id));

            Assert.Equal("partial", first.PaymentStatus);
            Assert.Equal(Money(product.Price - partialPaid), first.BalanceDue);

            var second = await pos.CheckoutAsync(new PosCheckoutRequest(
                Guid.NewGuid().ToString(),
                [new PosCheckoutLineRequest(product.ProductUnitId, 1m)],
                0m,
                [],
                "Second credit invoice",
                customer.Id));

            var customerBeforeCollection = await customers.GetCustomerAsync(customer.Id);
            Assert.NotNull(customerBeforeCollection);
            var firstBalance = first.BalanceDue;
            var secondBalance = second.BalanceDue;
            Assert.Equal(Money(firstBalance + secondBalance), customerBeforeCollection!.Customer.CurrentBalance);

            var collectionAmount = Money(firstBalance + secondBalance / 2m);
            var collectionKey = Guid.NewGuid().ToString();
            var collection = await customers.CollectAsync(new CustomerCollectionRequest(
                collectionKey,
                customer.Id,
                "cash",
                collectionAmount,
                collectionAmount + 10m,
                "COLLECT-001",
                null,
                "Oldest first"));

            Assert.Equal(10m, collection.ChangeAmount);
            Assert.Equal(2, collection.Allocations.Count);
            Assert.Equal(first.SaleId, collection.Allocations[0].SaleId);
            Assert.Equal(firstBalance, collection.Allocations[0].Amount);
            Assert.Equal(second.SaleId, collection.Allocations[1].SaleId);
            Assert.Equal(Money(secondBalance / 2m), collection.Allocations[1].Amount);

            var retry = await customers.CollectAsync(new CustomerCollectionRequest(
                collectionKey,
                customer.Id,
                "cash",
                collectionAmount,
                collectionAmount + 10m,
                "COLLECT-001",
                null,
                "Oldest first"));
            Assert.Equal(collection.Id, retry.Id);

            var factory = provider.GetRequiredService<IDbContextFactory<PosDbContext>>();
            await using var context = await factory.CreateDbContextAsync();
            var firstDb = await context.Sales.AsNoTracking().SingleAsync(x => x.Id == first.SaleId);
            var secondDb = await context.Sales.AsNoTracking().SingleAsync(x => x.Id == second.SaleId);
            Assert.Equal(0m, firstDb.BalanceDue);
            Assert.Equal("paid", firstDb.PaymentStatus);
            Assert.Equal(Money(secondBalance / 2m), secondDb.BalanceDue);
            Assert.Equal("partial", secondDb.PaymentStatus);

            var customerDb = await context.Customers.AsNoTracking().SingleAsync(x => x.Id == customer.Id);
            Assert.Equal(Money(secondBalance / 2m), customerDb.CurrentBalance);
            Assert.Single(await context.CustomerCollections.ToListAsync());
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public async Task Credit_return_reverses_receivable_before_refund_and_restores_stock()
    {
        var root = NewRoot();
        try
        {
            await using var provider = await BuildProviderAsync(root);
            var customers = provider.GetRequiredService<ICustomerService>();
            var pos = provider.GetRequiredService<IPosService>();
            var returns = provider.GetRequiredService<ISaleReturnService>();
            var sales = provider.GetRequiredService<ISalesService>();

            var customer = await customers.SaveCustomerAsync(new CustomerSaveRequest(
                null, "Return Credit Customer", "0700000004", null, null,
                5000m, 0m, true));
            var product = (await pos.SearchProductsAsync("6291001000001")).Single();
            var before = product.AvailableQuantity;

            var sale = await pos.CheckoutAsync(new PosCheckoutRequest(
                Guid.NewGuid().ToString(),
                [new PosCheckoutLineRequest(product.ProductUnitId, 2m)],
                0m,
                [],
                null,
                customer.Id));

            var detail = await sales.GetSaleAsync(sale.SaleId);
            Assert.NotNull(detail);
            var line = Assert.Single(detail!.Items);

            var returned = await returns.ReturnAsync(new SaleReturnRequest(
                Guid.NewGuid().ToString(),
                sale.SaleId,
                "Customer returned one unit",
                [new SaleReturnLineRequest(line.SaleItemId, 1m)],
                []));

            Assert.Equal("partially_returned", returned.SaleStatus);
            Assert.Equal(product.Price, returned.ReturnTotal);
            Assert.Equal(product.Price, returned.ReceivableReversed);
            Assert.Equal(0m, returned.RefundTotal);
            Assert.Equal(product.Price, returned.SaleBalanceDue);

            var customerAfter = await customers.GetCustomerAsync(customer.Id);
            Assert.NotNull(customerAfter);
            Assert.Equal(product.Price, customerAfter!.Customer.CurrentBalance);
            Assert.Contains(customerAfter.Ledger, x =>
                x.EntryType == "sale_return" && x.Credit == product.Price);

            var after = (await pos.SearchProductsAsync("6291001000001")).Single().AvailableQuantity;
            Assert.Equal(before - 1m, after);
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public async Task Paid_return_requires_refund_evidence_and_void_restores_remaining_quantity()
    {
        var root = NewRoot();
        try
        {
            await using var provider = await BuildProviderAsync(root);
            var pos = provider.GetRequiredService<IPosService>();
            var returns = provider.GetRequiredService<ISaleReturnService>();
            var sales = provider.GetRequiredService<ISalesService>();

            await pos.OpenShiftAsync(1000m);
            var product = (await pos.SearchProductsAsync("6291001000001")).Single();
            var before = product.AvailableQuantity;

            var sale = await pos.CheckoutAsync(new PosCheckoutRequest(
                Guid.NewGuid().ToString(),
                [new PosCheckoutLineRequest(product.ProductUnitId, 2m)],
                0m,
                [new PosPaymentRequest("cash", product.Price * 2m, product.Price * 2m)]));

            var detail = await sales.GetSaleAsync(sale.SaleId);
            var line = Assert.Single(detail!.Items);

            var evidenceError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                returns.ReturnAsync(new SaleReturnRequest(
                    Guid.NewGuid().ToString(),
                    sale.SaleId,
                    "Missing refund evidence",
                    [new SaleReturnLineRequest(line.SaleItemId, 1m)],
                    [])));
            Assert.Contains("Refund payment evidence", evidenceError.Message);

            var firstReturn = await returns.ReturnAsync(new SaleReturnRequest(
                Guid.NewGuid().ToString(),
                sale.SaleId,
                "Return first unit",
                [new SaleReturnLineRequest(line.SaleItemId, 1m)],
                [new SaleRefundRequest("cash", product.Price)]));

            Assert.Equal(product.Price, firstReturn.RefundTotal);
            Assert.Equal("partially_returned", firstReturn.SaleStatus);

            var voidResult = await returns.VoidAsync(new SaleVoidRequest(
                Guid.NewGuid().ToString(),
                sale.SaleId,
                "Void remaining unit",
                [new SaleRefundRequest("cash", product.Price)]));

            Assert.Equal("voided", voidResult.SaleStatus);
            Assert.Equal(product.Price, voidResult.ReturnTotal);
            Assert.Equal(product.Price, voidResult.RefundTotal);

            var after = (await pos.SearchProductsAsync("6291001000001")).Single().AvailableQuantity;
            Assert.Equal(before, after);

            var repeatedVoid = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                returns.VoidAsync(new SaleVoidRequest(
                    Guid.NewGuid().ToString(),
                    sale.SaleId,
                    "Cannot void twice",
                    [])));
            Assert.Contains("voided sale", repeatedVoid.Message, StringComparison.OrdinalIgnoreCase);
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
            .CreateOwnerAsync("Batch 07 Owner", "owner", "Password-123", "en");
        await provider.GetRequiredService<IUserSessionService>()
            .LoginAsync("owner", "Password-123");

        return provider;
    }

    private static async Task CreateCashierAsync(ServiceProvider provider)
    {
        var factory = provider.GetRequiredService<IDbContextFactory<PosDbContext>>();
        var hasher = provider.GetRequiredService<PasswordHasher>();
        await using var context = await factory.CreateDbContextAsync();

        if (await context.Users.AnyAsync(x => x.NormalizedUsername == "cashier7")) return;

        var role = await context.Roles.SingleAsync(x => x.Name == "cashier");
        var now = DateTimeOffset.UtcNow;
        var user = new UserEntity
        {
            Name = "Batch 07 Cashier",
            Username = "cashier7",
            NormalizedUsername = "cashier7",
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
        var root = Path.Combine(Path.GetTempPath(), "BusinessOS-POS-batch07-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void Cleanup(string root)
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    private static decimal Money(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private sealed class TestPaths(string root) : IApplicationPaths
    {
        public string RootPath { get; } = root;
        public string DatabasePath { get; } = Path.Combine(root, "batch07.db");
        public void EnsureCreated() => Directory.CreateDirectory(RootPath);
    }
}
