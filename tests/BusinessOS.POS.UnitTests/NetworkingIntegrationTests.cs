using BusinessOS.POS.Application.Abstractions.Authentication;
using BusinessOS.POS.Application.Abstractions.Networking;
using BusinessOS.POS.Application.Abstractions.Persistence;
using BusinessOS.POS.Application.Abstractions.Sales;
using BusinessOS.POS.Application.Abstractions.Storage;
using BusinessOS.POS.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BusinessOS.POS.UnitTests;

public sealed class NetworkingIntegrationTests
{
    [Fact]
    public async Task Pairing_is_one_time_secret_authenticated_and_revocation_disables_cash_terminal()
    {
        var root = NewRoot();
        try
        {
            await using var provider = await BuildProviderAsync(root);
            var terminals = provider.GetRequiredService<ILocalTerminalService>();
            var identity = await terminals.GetOrCreateServerIdentityAsync("Main POS Server");
            var code = await terminals.CreatePairingCodeAsync(TimeSpan.FromMinutes(5));
            var terminalId = Guid.NewGuid().ToString();

            var paired = await terminals.PairAsync(new PairTerminalRequest(
                code.Code, terminalId, "Counter Two", "POS-COUNTER-02", "cashier"));

            Assert.Equal(identity.ServerId, paired.ServerId);
            Assert.Equal(terminalId, paired.TerminalId);
            Assert.True(paired.TerminalSecret.Length >= 32);

            var authenticated = await terminals.AuthenticateTerminalAsync(
                paired.TerminalId, paired.TerminalSecret);
            Assert.NotNull(authenticated);
            Assert.NotNull(authenticated!.CashTerminalId);
            Assert.Equal("Counter Two", authenticated.Name);

            Assert.Null(await terminals.AuthenticateTerminalAsync(
                paired.TerminalId, "definitely-wrong-secret"));

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                terminals.PairAsync(new PairTerminalRequest(
                    code.Code, Guid.NewGuid().ToString(), "Replay", "REPLAY-PC", "cashier")));

            var cashTerminalId = authenticated.CashTerminalId!.Value;
            await terminals.RevokeAsync(paired.TerminalId);
            Assert.Null(await terminals.AuthenticateTerminalAsync(
                paired.TerminalId, paired.TerminalSecret));

            var factory = provider.GetRequiredService<IDbContextFactory<PosDbContext>>();
            await using var context = await factory.CreateDbContextAsync();
            var mapped = await context.Terminals.AsNoTracking()
                .SingleAsync(x => x.Id == cashTerminalId);
            Assert.False(mapped.IsActive);
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public async Task Paired_workstations_receive_independent_drawers_and_cash_sales_use_their_own_shift()
    {
        var root = NewRoot();
        try
        {
            await using var provider = await BuildProviderAsync(root);
            var terminals = provider.GetRequiredService<ILocalTerminalService>();
            await terminals.GetOrCreateServerIdentityAsync("Main POS Server");

            var first = await PairAsync(terminals, "Counter A", "POS-A");
            var second = await PairAsync(terminals, "Counter B", "POS-B");
            var registrations = await terminals.ListAsync();
            var firstRegistration = registrations.Single(x => x.TerminalId == first.TerminalId);
            var secondRegistration = registrations.Single(x => x.TerminalId == second.TerminalId);

            Assert.NotNull(firstRegistration.CashTerminalId);
            Assert.NotNull(secondRegistration.CashTerminalId);
            Assert.NotEqual(firstRegistration.CashTerminalId, secondRegistration.CashTerminalId);

            var factory = provider.GetRequiredService<IDbContextFactory<PosDbContext>>();
            var sessions = provider.GetRequiredService<IUserSessionService>();
            var authorizer = provider.GetRequiredService<IPermissionAuthorizer>();

            var posA = new LocalPosService(
                factory, sessions, authorizer,
                new FixedWorkstationContext(firstRegistration.CashTerminalId, first.TerminalId));
            var posB = new LocalPosService(
                factory, sessions, authorizer,
                new FixedWorkstationContext(secondRegistration.CashTerminalId, second.TerminalId));

            var shiftA = await posA.OpenShiftAsync(100m);
            var shiftB = await posB.OpenShiftAsync(200m);
            Assert.NotEqual(shiftA.ShiftId, shiftB.ShiftId);

            var product = (await posA.SearchProductsAsync("6291001000001")).Single();
            var saleA = await posA.CheckoutAsync(new PosCheckoutRequest(
                Guid.NewGuid().ToString(),
                [new PosCheckoutLineRequest(product.ProductUnitId, 1m)],
                0m,
                [new PosPaymentRequest("cash", product.Price, product.Price)]));
            var saleB = await posB.CheckoutAsync(new PosCheckoutRequest(
                Guid.NewGuid().ToString(),
                [new PosCheckoutLineRequest(product.ProductUnitId, 1m)],
                0m,
                [new PosPaymentRequest("cash", product.Price, product.Price)]));

            await using var context = await factory.CreateDbContextAsync();
            var persistedA = await context.Sales.AsNoTracking().SingleAsync(x => x.Id == saleA.SaleId);
            var persistedB = await context.Sales.AsNoTracking().SingleAsync(x => x.Id == saleB.SaleId);
            Assert.Equal(shiftA.ShiftId, persistedA.CashierShiftId);
            Assert.Equal(shiftB.ShiftId, persistedB.CashierShiftId);

            var drawerA = await context.CashierShifts.AsNoTracking().SingleAsync(x => x.Id == shiftA.ShiftId);
            var drawerB = await context.CashierShifts.AsNoTracking().SingleAsync(x => x.Id == shiftB.ShiftId);
            Assert.Equal(100m + product.Price, drawerA.ExpectedCash);
            Assert.Equal(200m + product.Price, drawerB.ExpectedCash);
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public async Task Lan_session_is_terminal_bound_permission_scoped_and_revoked_with_terminal()
    {
        var root = NewRoot();
        try
        {
            await using var provider = await BuildProviderAsync(root);
            var terminals = provider.GetRequiredService<ILocalTerminalService>();
            var sessions = provider.GetRequiredService<IUserSessionService>();
            var store = provider.GetRequiredService<ILanSessionStore>();
            await terminals.GetOrCreateServerIdentityAsync("Main POS Server");
            var paired = await PairAsync(terminals, "Restricted Counter", "POS-RESTRICTED");

            await terminals.UpdateAllowedPermissionsAsync(
                paired.TerminalId,
                new HashSet<string>(StringComparer.Ordinal) { "pos.access" });

            var issue = await store.CreateAsync(
                paired.TerminalId,
                sessions.Current!.UserId,
                TimeSpan.FromHours(1));

            var principal = await store.AuthenticateAsync(paired.TerminalId, issue.AccessToken);
            Assert.NotNull(principal);
            Assert.Contains("pos.access", principal!.User.Permissions);
            Assert.DoesNotContain("reports.view", principal.User.Permissions);
            Assert.Contains("pos.access", principal.TerminalPermissions);

            Assert.Null(await store.AuthenticateAsync(
                Guid.NewGuid().ToString(), issue.AccessToken));

            await terminals.RevokeAsync(paired.TerminalId);
            Assert.Null(await store.AuthenticateAsync(paired.TerminalId, issue.AccessToken));

            await terminals.ReactivateAsync(paired.TerminalId);
            Assert.Null(await store.AuthenticateAsync(paired.TerminalId, issue.AccessToken));
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public async Task Server_identity_is_persistent_while_display_name_can_change()
    {
        var root = NewRoot();
        try
        {
            await using var provider = await BuildProviderAsync(root);
            var terminals = provider.GetRequiredService<ILocalTerminalService>();

            var first = await terminals.GetOrCreateServerIdentityAsync("Main POS Server");
            var second = await terminals.GetOrCreateServerIdentityAsync("Front Office POS Server");

            Assert.Equal(first.ServerId, second.ServerId);
            Assert.Equal(first.CreatedAt, second.CreatedAt);
            Assert.Equal("Front Office POS Server", second.ServerName);
            Assert.True(second.UpdatedAt >= first.UpdatedAt);
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void Client_configuration_requires_valid_server_identity_certificate_and_terminal_uuid()
    {
        var invalid = new NetworkConfiguration
        {
            Mode = DeploymentMode.Client,
            IsConfigured = true,
            ServerHost = "192.168.1.20",
            ServerId = Guid.NewGuid().ToString(),
            ServerCertificateSha256 = new string('Z', 64),
            TerminalId = Guid.NewGuid().ToString(),
        };
        Assert.Throws<InvalidOperationException>(invalid.Validate);

        var missingHost = invalid with
        {
            ServerHost = null,
            ServerCertificateSha256 = new string('A', 64),
        };
        Assert.Throws<InvalidOperationException>(missingHost.Validate);

        var valid = invalid with
        {
            ServerCertificateSha256 = new string('A', 64),
        };
        valid.Validate();

        Assert.Throws<InvalidOperationException>(() =>
            (valid with { ServerPort = 80 }).Validate());
        Assert.Throws<InvalidOperationException>(() =>
            (valid with { DiscoveryPort = 70000 }).Validate());
    }

    private static async Task<PairTerminalResult> PairAsync(
        ILocalTerminalService terminals,
        string name,
        string computerName)
    {
        var code = await terminals.CreatePairingCodeAsync(TimeSpan.FromMinutes(5));
        return await terminals.PairAsync(new PairTerminalRequest(
            code.Code,
            Guid.NewGuid().ToString(),
            name,
            computerName,
            "cashier"));
    }

    private static async Task<ServiceProvider> BuildProviderAsync(string root)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IApplicationPaths>(new TestPaths(root));
        services.AddBusinessOSPosPersistence();
        var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<ILocalDatabaseInitializer>().InitializeAsync();
        await provider.GetRequiredService<IOwnerBootstrapService>()
            .CreateOwnerAsync("LAN Owner", "owner", "Password-123", "en");
        await provider.GetRequiredService<IUserSessionService>()
            .LoginAsync("owner", "Password-123");
        return provider;
    }

    private static string NewRoot()
    {
        var root = Path.Combine(
            Path.GetTempPath(), "BusinessOS-POS-lan-tests", Guid.NewGuid().ToString("N"));
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
        public string DatabasePath { get; } = Path.Combine(root, "lan.db");
        public void EnsureCreated() => Directory.CreateDirectory(RootPath);
    }

    private sealed class FixedWorkstationContext(
        long? cashTerminalId,
        string? lanTerminalId) : IWorkstationContext
    {
        public long? CashTerminalId { get; } = cashTerminalId;
        public string? LanTerminalId { get; } = lanTerminalId;
        public bool IsLanRequest => lanTerminalId is not null;
    }
}
