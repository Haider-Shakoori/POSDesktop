using BusinessOS.POS.Application.Abstractions.Authentication;
using BusinessOS.POS.Application.Abstractions.Persistence;
using BusinessOS.POS.Application.Abstractions.Sales;
using BusinessOS.POS.Application.Abstractions.Storage;
using BusinessOS.POS.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BusinessOS.POS.UnitTests;

public sealed class SalesIntegrationTests
{
    [Fact]
    public async Task Split_payment_tracks_applied_tendered_change_and_receipt_data()
    {
        var root = Path.Combine(Path.GetTempPath(), "BusinessOS-POS-sales-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            await using var provider = await BuildProviderAsync(root);
            var pos = provider.GetRequiredService<IPosService>();
            var sales = provider.GetRequiredService<ISalesService>();

            var product = (await pos.SearchProductsAsync("6291001000001")).Single();
            await pos.OpenShiftAsync(1000m);

            var total = product.Price;
            var bankAmount = 200m;
            var cashApplied = total - bankAmount;
            var cashTendered = cashApplied + 100m;

            var completed = await pos.CheckoutAsync(new PosCheckoutRequest(
                Guid.NewGuid().ToString(),
                [new PosCheckoutLineRequest(product.ProductUnitId, 1m)],
                0m,
                [
                    new PosPaymentRequest("bank", bankAmount, bankAmount, "BANK-REF-001", "Card terminal"),
                    new PosPaymentRequest("cash", cashApplied, cashTendered, null, "Cash received"),
                ],
                "Split payment sale"));

            Assert.Equal(total, completed.NetTotal);
            Assert.Equal(total, completed.PaidAmount);
            Assert.Equal(100m, completed.ChangeAmount);

            var history = await sales.GetSalesAsync(completed.SaleNumber);
            var row = Assert.Single(history);
            Assert.Equal(completed.SaleId, row.Id);
            Assert.Equal(100m, row.ChangeAmount);

            var detail = await sales.GetSaleAsync(completed.SaleId);
            Assert.NotNull(detail);
            Assert.Equal(2, detail!.Payments.Count);
            Assert.Equal(total, detail.Payments.Sum(x => x.AppliedAmount));

            var bank = detail.Payments.Single(x => x.MethodCode == "bank");
            Assert.Equal(bankAmount, bank.AppliedAmount);
            Assert.Equal(bankAmount, bank.TenderedAmount);
            Assert.Equal("BANK-REF-001", bank.Reference);

            var cash = detail.Payments.Single(x => x.MethodCode == "cash");
            Assert.Equal(cashApplied, cash.AppliedAmount);
            Assert.Equal(cashTendered, cash.TenderedAmount);
            Assert.Equal(100m, cash.ChangeAmount);

            var receipt = await sales.GetReceiptAsync(completed.SaleId);
            Assert.NotNull(receipt);
            Assert.Equal("AFN", receipt!.CurrencyCode);
            Assert.Equal(completed.SaleNumber, receipt.Sale.Number);
            Assert.Equal("Split payment sale", receipt.Sale.Notes);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Walk_in_partial_payment_requires_customer_and_non_cash_cannot_generate_change()
    {
        var root = Path.Combine(Path.GetTempPath(), "BusinessOS-POS-sales-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            await using var provider = await BuildProviderAsync(root);
            var pos = provider.GetRequiredService<IPosService>();
            var product = (await pos.SearchProductsAsync("6291001000001")).Single();

            var shortPayment = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                pos.CheckoutAsync(new PosCheckoutRequest(
                    Guid.NewGuid().ToString(),
                    [new PosCheckoutLineRequest(product.ProductUnitId, 1m)],
                    0m,
                    [new PosPaymentRequest("bank", product.Price - 1m)])));
            Assert.Contains("registered customer", shortPayment.Message);

            var nonCashChange = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                pos.CheckoutAsync(new PosCheckoutRequest(
                    Guid.NewGuid().ToString(),
                    [new PosCheckoutLineRequest(product.ProductUnitId, 1m)],
                    0m,
                    [new PosPaymentRequest("bank", product.Price, product.Price + 10m)])));
            Assert.Contains("only for cash", nonCashChange.Message);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Held_sale_can_be_released_without_deleting_history()
    {
        var root = Path.Combine(Path.GetTempPath(), "BusinessOS-POS-sales-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            await using var provider = await BuildProviderAsync(root);
            var pos = provider.GetRequiredService<IPosService>();
            var product = (await pos.SearchProductsAsync("6291001000001")).Single();

            var held = await pos.HoldAsync(new PosHoldRequest(
                Guid.NewGuid().ToString(),
                [new PosCheckoutLineRequest(product.ProductUnitId, 1m)],
                0m,
                "Customer will return"));

            Assert.Contains(await pos.GetHeldSalesAsync(), x => x.Id == held.Id);

            await pos.ReleaseHeldSaleAsync(held.Id);
            Assert.DoesNotContain(await pos.GetHeldSalesAsync(), x => x.Id == held.Id);

            var resume = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                pos.ResumeHeldSaleAsync(held.Id));
            Assert.Contains("active held sale", resume.Message);
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
            .CreateOwnerAsync("Sales Owner", "owner", "Password-123", "en");
        await provider.GetRequiredService<IUserSessionService>()
            .LoginAsync("owner", "Password-123");

        return provider;
    }

    private sealed class TestPaths(string root) : IApplicationPaths
    {
        public string RootPath { get; } = root;
        public string DatabasePath { get; } = Path.Combine(root, "sales.db");
        public void EnsureCreated() => Directory.CreateDirectory(RootPath);
    }
}
