using System.Collections.ObjectModel;
using BusinessOS.POS.Application.Abstractions.Networking;
using BusinessOS.POS.LocalClient;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BusinessOS.POS.Desktop.Networking;

public sealed partial class DeploymentSetupViewModel(
    INetworkConfigurationStore configurationStore,
    ILocalServerDiscovery discovery,
    LanTerminalPairingClient pairing) : ObservableObject
{
    public ObservableCollection<LocalServerDiscoveryAdvertisement> Servers { get; } = [];

    [ObservableProperty] private string serverName = "Main POS Server";
    [ObservableProperty] private LocalServerDiscoveryAdvertisement? selectedServer;
    [ObservableProperty] private string pairingCode = string.Empty;
    [ObservableProperty] private string terminalName = Environment.MachineName + " POS";
    [ObservableProperty] private string terminalRole = "POS Terminal";
    [ObservableProperty] private string manualServerName = "Main POS Server";
    [ObservableProperty] private string manualServerHost = string.Empty;
    [ObservableProperty] private int manualServerPort = NetworkConfiguration.DefaultServerPort;
    [ObservableProperty] private string manualServerId = string.Empty;
    [ObservableProperty] private string manualCertificateSha256 = string.Empty;
    [ObservableProperty] private string statusMessage = "Choose how this computer will run BusinessOS POS.";
    [ObservableProperty] private bool isBusy;

    public async Task ConfigureStandaloneAsync()
    {
        await BusyAsync(async () =>
        {
            var current = await configurationStore.LoadAsync();
            await configurationStore.SaveAsync(current with
            {
                Mode = DeploymentMode.Standalone,
                IsConfigured = true,
            });
            StatusMessage = "Standalone mode configured.";
        });
    }

    public async Task ConfigureServerAsync()
    {
        await BusyAsync(async () =>
        {
            var current = await configurationStore.LoadAsync();
            await configurationStore.SaveAsync(current with
            {
                Mode = DeploymentMode.Server,
                ServerName = ServerName.Trim(),
                ServerHost = Environment.MachineName,
                IsConfigured = true,
            });
            StatusMessage = "Main POS Server mode configured.";
        });
    }

    public async Task DiscoverAsync()
    {
        await BusyAsync(async () =>
        {
            var current = await configurationStore.LoadAsync();
            Servers.Clear();
            foreach (var server in await discovery.DiscoverAsync(
                         current.DiscoveryPort, TimeSpan.FromSeconds(3)))
                Servers.Add(server);
            SelectedServer = Servers.FirstOrDefault();
            StatusMessage = Servers.Count == 0
                ? "No POS server answered local discovery."
                : Servers.Count + " server(s) found.";
        });
    }

    public void UseManualServer()
    {
        var host = ManualServerHost.Trim();
        var serverId = ManualServerId.Trim();
        var fingerprint = ManualCertificateSha256
            .Replace(":", string.Empty)
            .Replace(" ", string.Empty)
            .Trim()
            .ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(host))
            throw new InvalidOperationException("Enter the Main POS Server host or IP address.");
        if (!Guid.TryParse(serverId, out _))
            throw new InvalidOperationException("Enter the server UUID shown on the Main POS Server.");
        if (ManualServerPort is < 1024 or > 65535)
            throw new InvalidOperationException("The server port must be between 1024 and 65535.");
        if (fingerprint.Length != 64 || fingerprint.Any(ch => !Uri.IsHexDigit(ch)))
            throw new InvalidOperationException("Enter the 64-character SHA-256 certificate fingerprint shown on the Main POS Server.");

        var manual = new LocalServerDiscoveryAdvertisement(
            "BusinessOS.POS.LocalServer", "v1", serverId,
            string.IsNullOrWhiteSpace(ManualServerName) ? "Main POS Server" : ManualServerName.Trim(),
            host, ManualServerPort, fingerprint);

        var existing = Servers.FirstOrDefault(x => x.ServerId == manual.ServerId);
        if (existing is not null) Servers.Remove(existing);
        Servers.Insert(0, manual);
        SelectedServer = manual;
        StatusMessage = "Manual server selected. Verify its identity and fingerprint before pairing.";
    }

    public async Task PairClientAsync()
    {
        if (SelectedServer is null)
        {
            StatusMessage = "Select a discovered Main POS Server first.";
            return;
        }

        await BusyAsync(async () =>
        {
            await pairing.PairAsync(
                SelectedServer, PairingCode, TerminalName, TerminalRole);
            PairingCode = string.Empty;
            StatusMessage = "Client terminal paired securely.";
        });
    }

    private async Task BusyAsync(Func<Task> action)
    {
        if (IsBusy) return;
        IsBusy = true;
        try { await action(); }
        catch (Exception ex) { StatusMessage = ex.Message; throw; }
        finally { IsBusy = false; }
    }
}
