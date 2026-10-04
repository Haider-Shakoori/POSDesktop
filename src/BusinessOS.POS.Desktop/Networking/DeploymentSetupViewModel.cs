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
