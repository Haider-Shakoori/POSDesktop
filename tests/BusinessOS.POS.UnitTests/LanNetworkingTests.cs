using Xunit;
using BusinessOS.POS.Application.Abstractions.Networking;
using BusinessOS.POS.Application.Abstractions.Storage;
using BusinessOS.POS.Infrastructure.Networking;

namespace BusinessOS.POS.UnitTests;

public sealed class LanNetworkingTests : IDisposable
{
    private readonly TestPaths _paths = new();

    [Fact]
    public void Configured_client_requires_paired_server_identity()
    {
        var config = new NetworkConfiguration { Mode = DeploymentMode.Client, IsConfigured = true };
        Assert.Throws<InvalidOperationException>(() => config.Validate());
    }

    [Fact]
    public async Task Pairing_code_is_one_time_and_registered_terminal_can_authenticate()
    {
        var registry = new FileLanTerminalRegistry(_paths);
        var serverId = await registry.GetOrCreateServerIdAsync();
        var issue = await registry.CreatePairingCodeAsync(TimeSpan.FromMinutes(10));
        var paired = await registry.PairAsync(issue.Code, "terminal-1", "Front Counter", "POS-PC-01");

        Assert.Equal(serverId, paired.ServerId);
        Assert.True(await registry.AuthenticateAsync(paired.TerminalId, paired.TerminalSecret));
        Assert.Single(await registry.ListAsync());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            registry.PairAsync(issue.Code, "terminal-2", "Second", "POS-PC-02"));
    }

    [Fact]
    public async Task Revoked_terminal_can_no_longer_authenticate()
    {
        var registry = new FileLanTerminalRegistry(_paths);
        var issue = await registry.CreatePairingCodeAsync(TimeSpan.FromMinutes(10));
        var paired = await registry.PairAsync(issue.Code, "terminal-1", "Counter", "PC");

        await registry.RevokeAsync(paired.TerminalId);

        Assert.False(await registry.AuthenticateAsync(paired.TerminalId, paired.TerminalSecret));
        Assert.False((await registry.ListAsync()).Single().IsActive);
    }

    public void Dispose()
    {
        if (Directory.Exists(_paths.RootPath)) Directory.Delete(_paths.RootPath, true);
    }

    private sealed class TestPaths : IApplicationPaths
    {
        public TestPaths()
        {
            RootPath = Path.Combine(Path.GetTempPath(), "businessos-pos-tests", Guid.NewGuid().ToString("N"));
            DatabasePath = Path.Combine(RootPath, "db.sqlite");
            NetworkConfigurationPath = Path.Combine(RootPath, "network.json");
            NetworkSecretsPath = Path.Combine(RootPath, "secrets.dat");
            ServerCertificatePath = Path.Combine(RootPath, "server.pfx");
            LanTerminalsPath = Path.Combine(RootPath, "terminals.json");
        }
        public string RootPath { get; }
        public string DatabasePath { get; }
        public string NetworkConfigurationPath { get; }
        public string NetworkSecretsPath { get; }
        public string ServerCertificatePath { get; }
        public string LanTerminalsPath { get; }
        public void EnsureCreated() => Directory.CreateDirectory(RootPath);
    }
}
