using BusinessOS.POS.Application.Abstractions.Authentication;
using BusinessOS.POS.Application.Abstractions.Persistence;
using BusinessOS.POS.Application.Abstractions.Sales;
using BusinessOS.POS.Application.Abstractions.Storage;
using BusinessOS.POS.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BusinessOS.POS.UnitTests;

public sealed class CorePosIntegrationTests
{
    [Fact]
    public async Task Seeded_barcode_shift_and_checkout_work_end_to_end()
    {
        var root = Path.Combine(Path.GetTempPath(), "BusinessOS-POS-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var services = new ServiceCollection();
            services.AddSingleton<IApplicationPaths>(new TestPaths(root));
            services.AddBusinessOSPosPersistence();

            await using var provider = services.BuildServiceProvider();

            await provider.GetRequiredService<ILocalDatabaseInitializer>().InitializeAsync();
            var bootstrap = provider.GetRequiredService<IOwnerBootstrapService>();
            await bootstrap.CreateOwnerAsync("Test Owner", "owner", "Password-123", "en");

            var sessions = provider.GetRequiredService<IUserSessionService>();
            await sessions.LoginAsync("owner", "Password-123");

            var pos = provider.GetRequiredService<IPosService>();
            var barcodeResult = await pos.SearchProductsAsync("6291001000001");

            Assert.Single(barcodeResult);
            Assert.Equal("DEMO-0001", barcodeResult[0].Sku);

            var shift = await pos.OpenShiftAsync(1000m);
            Assert.True(shift.IsOpen);

            var before = barcodeResult[0].AvailableQuantity;
            var sale = await pos.CheckoutAsync(new PosCheckoutRequest(
                Guid.NewGuid().ToString(),
                [new PosCheckoutLineRequest(barcodeResult[0].ProductUnitId, 2m)],
                0m,
                [new PosPaymentRequest("cash", barcodeResult[0].Price * 2m)]));

            Assert.StartsWith("SAL-", sale.SaleNumber);
            Assert.Equal(barcodeResult[0].Price * 2m, sale.NetTotal);
            Assert.True(sale.CogsTotal > 0m);

            var after = (await pos.SearchProductsAsync("6291001000001")).Single().AvailableQuantity;
            Assert.Equal(before - 2m, after);
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
    public async Task Cash_checkout_requires_an_open_shift()
    {
        var root = Path.Combine(Path.GetTempPath(), "BusinessOS-POS-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var services = new ServiceCollection();
            services.AddSingleton<IApplicationPaths>(new TestPaths(root));
            services.AddBusinessOSPosPersistence();
            await using var provider = services.BuildServiceProvider();

            await provider.GetRequiredService<ILocalDatabaseInitializer>().InitializeAsync();
            await provider.GetRequiredService<IOwnerBootstrapService>()
                .CreateOwnerAsync("Test Owner", "owner", "Password-123", "en");
            await provider.GetRequiredService<IUserSessionService>()
                .LoginAsync("owner", "Password-123");

            var pos = provider.GetRequiredService<IPosService>();
            var product = (await pos.SearchProductsAsync("6291001000001")).Single();

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                pos.CheckoutAsync(new PosCheckoutRequest(
                    Guid.NewGuid().ToString(),
                    [new PosCheckoutLineRequest(product.ProductUnitId, 1m)],
                    0m,
                    [new PosPaymentRequest("cash", product.Price)])));

            Assert.Contains("Open a cashier shift", exception.Message);
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
        public string DatabasePath { get; } = Path.Combine(root, "test.db");
        public void EnsureCreated() => Directory.CreateDirectory(RootPath);
    }
}
