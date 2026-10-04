using System.Collections.ObjectModel;
using BusinessOS.POS.Application.Abstractions.Authentication;
using BusinessOS.POS.Application.Abstractions.Networking;
using BusinessOS.POS.LocalClient;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace BusinessOS.POS.Desktop.Networking;

public sealed partial class TerminalsViewModel : ObservableObject
{
    private readonly INetworkConfigurationStore _configurationStore;
    private readonly ILocalServerDiscovery _discovery;
    private readonly LanTerminalPairingClient _pairing;
    private readonly IServiceProvider _services;
    private readonly IPermissionAuthorizer _authorizer;
    private bool _loaded;

    public TerminalsViewModel(
        INetworkConfigurationStore configurationStore,
        ILocalServerDiscovery discovery,
        LanTerminalPairingClient pairing,
        IServiceProvider services,
        IPermissionAuthorizer authorizer)
    {
        _configurationStore = configurationStore;
        _discovery = discovery;
        _pairing = pairing;
        _services = services;
        _authorizer = authorizer;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        SaveConfigurationCommand = new AsyncRelayCommand(SaveConfigurationAsync);
        CreatePairingCodeCommand = new AsyncRelayCommand(CreatePairingCodeAsync);
        DiscoverCommand = new AsyncRelayCommand(DiscoverAsync);
        PairCommand = new AsyncRelayCommand(PairAsync);
        RevokeCommand = new AsyncRelayCommand(RevokeAsync);
    }

    public DeploymentMode[] Modes { get; } = Enum.GetValues<DeploymentMode>();
    public ObservableCollection<RegisteredTerminal> Terminals { get; } = [];
    public ObservableCollection<LocalServerDiscoveryAdvertisement> DiscoveredServers { get; } = [];

    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand SaveConfigurationCommand { get; }
    public IAsyncRelayCommand CreatePairingCodeCommand { get; }
    public IAsyncRelayCommand DiscoverCommand { get; }
    public IAsyncRelayCommand PairCommand { get; }
    public IAsyncRelayCommand RevokeCommand { get; }

    [ObservableProperty] private DeploymentMode selectedMode;
    [ObservableProperty] private string serverName = "Main POS Server";
    [ObservableProperty] private int serverPort = NetworkConfiguration.DefaultServerPort;
    [ObservableProperty] private int discoveryPort = NetworkConfiguration.DefaultDiscoveryPort;
    [ObservableProperty] private bool discoveryEnabled = true;
    [ObservableProperty] private string? configuredServerHost;
    [ObservableProperty] private string? configuredServerId;
    [ObservableProperty] private string? configuredTerminalId;
    [ObservableProperty] private string? configuredTerminalName;
    [ObservableProperty] private string pairingCode = string.Empty;
    [ObservableProperty] private string terminalName = Environment.MachineName + " POS";
    [ObservableProperty] private string terminalRole = "POS Terminal";
    [ObservableProperty] private string? issuedPairingCode;
    [ObservableProperty] private DateTimeOffset? pairingExpiresAt;
    [ObservableProperty] private LocalServerDiscoveryAdvertisement? selectedDiscoveredServer;
    [ObservableProperty] private RegisteredTerminal? selectedTerminal;
    [ObservableProperty] private string statusMessage = "Ready.";
    [ObservableProperty] private bool isBusy;

    public bool IsServerMode => SelectedMode == DeploymentMode.Server;
    public bool IsClientMode => SelectedMode == DeploymentMode.Client;

    partial void OnSelectedModeChanged(DeploymentMode value)
    {
        OnPropertyChanged(nameof(IsServerMode));
        OnPropertyChanged(nameof(IsClientMode));
    }

    public async Task InitializeAsync()
    {
        if (_loaded) return;
        _loaded = true;
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        await BusyAsync(async () =>
        {
            var cfg = await _configurationStore.LoadAsync();
            SelectedMode = cfg.Mode;
            ServerName = cfg.ServerName;
            ServerPort = cfg.ServerPort;
            DiscoveryPort = cfg.DiscoveryPort;
            DiscoveryEnabled = cfg.DiscoveryEnabled;
            ConfiguredServerHost = cfg.ServerHost;
            ConfiguredServerId = cfg.ServerId;
            ConfiguredTerminalId = cfg.TerminalId;
            ConfiguredTerminalName = cfg.TerminalName;
            await RefreshTerminalListAsync();
            StatusMessage = cfg.Mode + " configuration loaded.";
        });
    }

    private async Task SaveConfigurationAsync()
    {
        _authorizer.Demand("users.manage");
        await BusyAsync(async () =>
        {
            var current = await _configurationStore.LoadAsync();
            var next = current with
            {
                Mode = SelectedMode,
                ServerName = ServerName.Trim(),
                ServerPort = ServerPort,
                DiscoveryPort = DiscoveryPort,
                DiscoveryEnabled = DiscoveryEnabled,
                IsConfigured = SelectedMode == DeploymentMode.Client
                    ? current.IsConfigured && !string.IsNullOrWhiteSpace(current.TerminalId)
                    : true,
            };

            if (SelectedMode == DeploymentMode.Server)
            {
                var terminals = _services.GetService<ILocalTerminalService>()
                    ?? throw new InvalidOperationException("Server services are available after restarting in Server mode.");
                var identity = await terminals.GetOrCreateServerIdentityAsync(next.ServerName);
                next = next with
                {
                    ServerHost = Environment.MachineName,
                    ServerId = identity.ServerId,
                    IsConfigured = true,
                };
            }

            await _configurationStore.SaveAsync(next);
            StatusMessage = "Network configuration saved. Restart BusinessOS POS to apply a deployment-mode change.";
        });
    }

    private async Task CreatePairingCodeAsync()
    {
        _authorizer.Demand("users.manage");
        await BusyAsync(async () =>
        {
            var terminals = _services.GetService<ILocalTerminalService>()
                ?? throw new InvalidOperationException("Pairing codes can be issued only on the Main POS Server.");
            var identity = await terminals.GetOrCreateServerIdentityAsync(ServerName);
            var issued = await terminals.CreatePairingCodeAsync(TimeSpan.FromMinutes(5));
            IssuedPairingCode = issued.Code;
            PairingExpiresAt = issued.ExpiresAt;
            ConfiguredServerId = identity.ServerId;
            await RefreshTerminalListAsync();
            StatusMessage = "One-time pairing code created. It expires in 5 minutes.";
        });
    }

    private async Task DiscoverAsync()
    {
        await BusyAsync(async () =>
        {
            DiscoveredServers.Clear();
            foreach (var server in await _discovery.DiscoverAsync(
                         DiscoveryPort, TimeSpan.FromSeconds(3)))
                DiscoveredServers.Add(server);
            SelectedDiscoveredServer = DiscoveredServers.FirstOrDefault();
            StatusMessage = DiscoveredServers.Count == 0
                ? "No POS server answered discovery. You can also use manual server configuration."
                : DiscoveredServers.Count + " POS server(s) discovered.";
        });
    }

    private async Task PairAsync()
    {
        _authorizer.Demand("users.manage");
        if (SelectedDiscoveredServer is null)
        {
            StatusMessage = "Select a discovered POS server first.";
            return;
        }

        await BusyAsync(async () =>
        {
            var result = await _pairing.PairAsync(
                SelectedDiscoveredServer,
                PairingCode,
                TerminalName,
                TerminalRole);
            ConfiguredTerminalId = result.TerminalId;
            ConfiguredServerId = result.ServerId;
            ConfiguredServerHost = SelectedDiscoveredServer.HostName;
            ConfiguredTerminalName = TerminalName;
            PairingCode = string.Empty;
            StatusMessage = "Terminal paired securely. Restart BusinessOS POS to enter Client mode.";
        });
    }

    private async Task RevokeAsync()
    {
        _authorizer.Demand("users.manage");
        if (SelectedTerminal is null) return;
        await BusyAsync(async () =>
        {
            var terminals = _services.GetService<ILocalTerminalService>()
                ?? throw new InvalidOperationException("Terminal revocation is available only on the Main POS Server.");
            await terminals.RevokeAsync(SelectedTerminal.TerminalId);
            await RefreshTerminalListAsync();
            StatusMessage = "Terminal access revoked.";
        });
    }

    private async Task RefreshTerminalListAsync()
    {
        Terminals.Clear();
        var service = _services.GetService<ILocalTerminalService>();
        if (service is null) return;
        foreach (var row in await service.ListAsync()) Terminals.Add(row);
    }

    private async Task BusyAsync(Func<Task> action)
    {
        if (IsBusy) return;
        IsBusy = true;
        try { await action(); }
        catch (Exception ex) { StatusMessage = ex.Message; }
        finally { IsBusy = false; }
    }
}
