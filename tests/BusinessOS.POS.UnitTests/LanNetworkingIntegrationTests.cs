using BusinessOS.POS.Application.Abstractions.Authentication;
using BusinessOS.POS.Application.Abstractions.Catalog;
using BusinessOS.POS.Application.Abstractions.Cash;
using BusinessOS.POS.Application.Abstractions.Closing;
using BusinessOS.POS.Application.Abstractions.Customers;
using BusinessOS.POS.Application.Abstractions.Dashboard;
using BusinessOS.POS.Application.Abstractions.Inventory;
using BusinessOS.POS.Application.Abstractions.Networking;
using BusinessOS.POS.Application.Abstractions.Persistence;
using BusinessOS.POS.Application.Abstractions.Purchasing;
using BusinessOS.POS.Application.Abstractions.Reporting;
using BusinessOS.POS.Application.Abstractions.Sales;
using BusinessOS.POS.Application.Abstractions.Storage;
using BusinessOS.POS.Infrastructure.Networking;
using BusinessOS.POS.LocalClient;
using BusinessOS.POS.LocalServer.Authentication;
using BusinessOS.POS.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BusinessOS.POS.UnitTests;

public sealed class LanNetworkingIntegrationTests
{
    [Fact]
    public async Task Network_configuration_round_trips_without_storing_terminal_secret()
    {
        var root = NewRoot();
        try
        {
            var paths = new TestPaths(root);
            var store = new NetworkConfigurationStore(paths);
            var cfg = new NetworkConfiguration
            {
                Mode = DeploymentMode.Client,
                ServerHost = "192.168.1.10",
                ServerPort = 5380,
                ServerId = Guid.CreateVersion7().ToString(),
                ServerCertificateSha256 = new string('A', 64),
                TerminalId = Guid.CreateVersion7().ToString(),
                TerminalName = "Counter 2",
                TerminalRole = "POS Terminal",
                IsConfigured = true,
            };

            await store.SaveAsync(cfg);
            var loaded = await store.LoadAsync();

            Assert.Equal(cfg, loaded);
            var json = await File.ReadAllTextAsync(Path.Combine(root, "network.json"));
            Assert.DoesNotContain("terminal-secret", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public async Task Server_identity_is_persistent_and_pairing_code_is_one_time()
    {
        var root = NewRoot();
        try
        {
            await using var provider = await BuildServerProviderAsync(root);
            var terminals = provider.GetRequiredService<ILocalTerminalService>();

            var first = await terminals.GetOrCreateServerIdentityAsync("Main POS Server");
            var second = await terminals.GetOrCreateServerIdentityAsync("Renamed POS Server");
            Assert.Equal(first.ServerId, second.ServerId);
            Assert.Equal("Renamed POS Server", second.ServerName);

            var pairing = await terminals.CreatePairingCodeAsync(TimeSpan.FromMinutes(5));
            var terminalId = Guid.CreateVersion7().ToString();
            var paired = await terminals.PairAsync(new PairTerminalRequest(
                pairing.Code, terminalId, "Front Counter", "POS-PC-02", "POS Terminal"));

            Assert.Equal(terminalId.ToLowerInvariant(), paired.TerminalId);
            Assert.Equal(first.ServerId, paired.ServerId);
            Assert.False(string.IsNullOrWhiteSpace(paired.TerminalSecret));

            var authenticated = await terminals.AuthenticateTerminalAsync(
                paired.TerminalId, paired.TerminalSecret);
            Assert.NotNull(authenticated);
            Assert.Equal("Front Counter", authenticated!.Name);

            Assert.Null(await terminals.AuthenticateTerminalAsync(
                paired.TerminalId, "wrong-terminal-secret"));

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                terminals.PairAsync(new PairTerminalRequest(
                    pairing.Code, Guid.CreateVersion7().ToString(),
                    "Replay", "REPLAY-PC", "POS Terminal")));
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public async Task Revoked_terminal_cannot_authenticate()
    {
        var root = NewRoot();
        try
        {
            await using var provider = await BuildServerProviderAsync(root);
            var terminals = provider.GetRequiredService<ILocalTerminalService>();
            await terminals.GetOrCreateServerIdentityAsync("Main POS Server");
            var pairing = await terminals.CreatePairingCodeAsync(TimeSpan.FromMinutes(5));
            var paired = await terminals.PairAsync(new PairTerminalRequest(
                pairing.Code, Guid.CreateVersion7().ToString(),
                "Manager Office", "MANAGER-PC", "Manager Terminal"));

            await terminals.RevokeAsync(paired.TerminalId);

            Assert.Null(await terminals.AuthenticateTerminalAsync(
                paired.TerminalId, paired.TerminalSecret));
            var listing = Assert.Single(await terminals.ListAsync());
            Assert.False(listing.IsActive);
            Assert.NotNull(listing.RevokedAt);
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public async Task Lan_user_session_is_bound_to_paired_terminal()
    {
        var root = NewRoot();
        try
        {
            await using var provider = await BuildServerProviderAsync(root);
            var terminals = provider.GetRequiredService<ILocalTerminalService>();
            await terminals.GetOrCreateServerIdentityAsync("Main POS Server");
            var pairing = await terminals.CreatePairingCodeAsync(TimeSpan.FromMinutes(5));
            var paired = await terminals.PairAsync(new PairTerminalRequest(
                pairing.Code, Guid.CreateVersion7().ToString(),
                "Counter 3", "POS-PC-03", "POS Terminal"));

            var auth = provider.GetRequiredService<LanAuthenticationService>();
            var login = await auth.LoginAsync(
                paired.TerminalId, "owner", "Password-123");

            var current = await auth.AuthenticateSessionAsync(
                paired.TerminalId, login.SessionToken);
            Assert.NotNull(current);
            Assert.Equal("owner", current!.Username);

            Assert.Null(await auth.AuthenticateSessionAsync(
                Guid.CreateVersion7().ToString(), login.SessionToken));

            await auth.LogoutAsync(paired.TerminalId, login.SessionToken);
            Assert.Null(await auth.AuthenticateSessionAsync(
                paired.TerminalId, login.SessionToken));
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void Client_service_graph_has_remote_business_services_but_no_authoritative_database()
    {
        var services = new ServiceCollection();
        services.AddSingleton<INetworkConfigurationStore>(new FakeConfigurationStore());
        services.AddSingleton<INetworkSecretStore>(new FakeSecretStore());
        services.AddBusinessOSPosLocalClient();

        using var provider = services.BuildServiceProvider();

        Assert.Null(provider.GetService<ILocalDatabaseInitializer>());
        Assert.NotNull(provider.GetRequiredService<IUserSessionService>());
        Assert.NotNull(provider.GetRequiredService<IProductCatalogService>());
        Assert.NotNull(provider.GetRequiredService<IInventoryService>());
        Assert.NotNull(provider.GetRequiredService<IPosService>());
        Assert.NotNull(provider.GetRequiredService<ISalesService>());
        Assert.NotNull(provider.GetRequiredService<ISaleReturnService>());
        Assert.NotNull(provider.GetRequiredService<ICustomerService>());
        Assert.NotNull(provider.GetRequiredService<IPurchasingService>());
        Assert.NotNull(provider.GetRequiredService<ICashManagementService>());
        Assert.NotNull(provider.GetRequiredService<IBusinessDayClosingService>());
        Assert.NotNull(provider.GetRequiredService<IDashboardService>());
        Assert.NotNull(provider.GetRequiredService<IReportingService>());
    }

    [Fact]
    public void Configured_client_requires_server_identity_certificate_and_terminal_identity()
    {
        var missingPin = new NetworkConfiguration
        {
            Mode = DeploymentMode.Client,
            ServerHost = "POS-SERVER",
            ServerId = Guid.CreateVersion7().ToString(),
            TerminalId = Guid.CreateVersion7().ToString(),
            IsConfigured = true,
        };

        Assert.Throws<InvalidOperationException>(missingPin.Validate);

        var invalidPort = new NetworkConfiguration
        {
            Mode = DeploymentMode.Server,
            ServerPort = 80,
            IsConfigured = true,
        };

        Assert.Throws<InvalidOperationException>(invalidPort.Validate);
    }

    private static async Task<ServiceProvider> BuildServerProviderAsync(string root)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IApplicationPaths>(new TestPaths(root));
        services.AddBusinessOSPosPersistence();
        services.AddSingleton<LanAuthenticationService>();
        var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<ILocalDatabaseInitializer>().InitializeAsync();
        await provider.GetRequiredService<IOwnerBootstrapService>()
            .CreateOwnerAsync("LAN Owner", "owner", "Password-123", "en");
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

    private sealed class FakeConfigurationStore : INetworkConfigurationStore
    {
        private NetworkConfiguration _value = new()
        {
            Mode = DeploymentMode.Client,
            ServerHost = "127.0.0.1",
            ServerId = "server",
            ServerCertificateSha256 = new string('A', 64),
            TerminalId = Guid.NewGuid().ToString(),
            IsConfigured = true,
        };

        public Task<NetworkConfiguration> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_value);

        public Task SaveAsync(NetworkConfiguration configuration, CancellationToken cancellationToken = default)
        {
            _value = configuration;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSecretStore : INetworkSecretStore
    {
        private readonly Dictionary<string, string> _values = [];

        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(_values.GetValueOrDefault(key));

        public Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
        {
            _values[key] = value;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            _values.Remove(key);
            return Task.CompletedTask;
        }
    }
}
