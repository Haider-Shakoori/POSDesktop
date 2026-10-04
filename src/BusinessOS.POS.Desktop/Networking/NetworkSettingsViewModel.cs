using System.Collections.ObjectModel;
using BusinessOS.POS.Application.Abstractions.Networking;
using BusinessOS.POS.LocalClient;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace BusinessOS.POS.Desktop.Networking;

public sealed partial class NetworkSettingsViewModel : ObservableObject
{
    private readonly INetworkConfigurationStore _configurationStore;
    private readonly ILocalServerDiscovery _discovery;
    private readonly LanTerminalPairingClient _pairing;
    private readonly IServiceProvider _services;
    private bool _loaded;

    public NetworkSettingsViewModel(
        INetworkConfigurationStore configurationStore,
        ILocalServerDiscovery discovery,
        LanTerminalPairingClient pairing,
        IServiceProvider services)
    {
        _configurationStore = configurationStore;
        _discovery = discovery;
        _pairing = pairing;
        _services = services;

        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        DiscoverCommand = new AsyncRelayCommand(DiscoverAsync);
        SaveStandaloneCommand = new AsyncRelayCommand(SaveStandaloneAsync);
        SaveServerCommand = new AsyncRelayCommand(SaveServerAsync);
        IssuePairingCodeCommand = new AsyncRelayCommand(IssuePairingCodeAsync);
        PairCommand = new AsyncRelayCommand(PairAsync);
        TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync);
        RevokeTerminalCommand = new AsyncRelayCommand(RevokeTerminalAsync);
        ReactivateTerminalCommand = new AsyncRelayCommand(ReactivateTerminalAsync);
    }

    public ObservableCollection<LocalServerDiscoveryAdvertisement> DiscoveredServers { get; } = [];
    public ObservableCollection<RegisteredLanTerminal> RegisteredTerminals { get; } = [];

    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand DiscoverCommand { get; }
    public IAsyncRelayCommand SaveStandaloneCommand { get; }
    public IAsyncRelayCommand SaveServerCommand { get; }
    public IAsyncRelayCommand IssuePairingCodeCommand { get; }
    public IAsyncRelayCommand PairCommand { get; }
    public IAsyncRelayCommand TestConnectionCommand { get; }
    public IAsyncRelayCommand RevokeTerminalCommand { get; }
    public IAsyncRelayCommand ReactivateTerminalCommand { get; }

    [ObservableProperty] private DeploymentMode mode = DeploymentMode.Standalone;
    [ObservableProperty] private string serverName = "Main POS Server";
    [ObservableProperty] private int serverPort = NetworkConfiguration.DefaultServerPort;
    [ObservableProperty] private int discoveryPort = NetworkConfiguration.DefaultDiscoveryPort;
    [ObservableProperty] private bool discoveryEnabled = true;
    [ObservableProperty] private string? serverId;
    [ObservableProperty] private string? certificateFingerprint;
    [ObservableProperty] private string manualHost = string.Empty;
    [ObservableProperty] private string manualServerId = string.Empty;
    [ObservableProperty] private string manualFingerprint = string.Empty;
    [ObservableProperty] private string pairingCode = string.Empty;
    [ObservableProperty] private string terminalName = Environment.MachineName;
    [ObservableProperty] private string terminalRole = "POS Terminal";
    [ObservableProperty] private LocalServerDiscoveryAdvertisement? selectedServer;
    [ObservableProperty] private RegisteredLanTerminal? selectedTerminal;
    [ObservableProperty] private string issuedPairingCode = "—";
    [ObservableProperty] private string pairingExpiry = string.Empty;
    [ObservableProperty] private string connectionStatus = "Not tested";
    [ObservableProperty] private string statusMessage = "Ready.";
    [ObservableProperty] private bool isBusy;

    public bool IsServerMode => Mode == DeploymentMode.Server;
    public bool IsClientMode => Mode == DeploymentMode.Client;
    public bool IsStandaloneMode => Mode == DeploymentMode.Standalone;
    public bool CanAdministerServer => _services.GetService<ILocalTerminalService>() is not null;
    public bool CanLeaveClientMode => Mode != DeploymentMode.Client;

    partial void OnModeChanged(DeploymentMode value)
    {
        OnPropertyChanged(nameof(IsServerMode));
        OnPropertyChanged(nameof(IsClientMode));
        OnPropertyChanged(nameof(IsStandaloneMode));
        OnPropertyChanged(nameof(CanLeaveClientMode));
    }

    partial void OnSelectedServerChanged(LocalServerDiscoveryAdvertisement? value)
    {
        if (value is null) return;
        ManualHost = value.HostName;
        ManualServerId = value.ServerId;
        ManualFingerprint = value.CertificateSha256;
        ServerPort = value.Port;
    }

    public async Task InitializeAsync()
    {
        if (_loaded) return;
        _loaded = true;
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        await ExecuteAsync(async () =>
        {
            var configuration = await _configurationStore.LoadAsync();
            Mode = configuration.Mode;
            ServerName = configuration.ServerName;
            ServerPort = configuration.ServerPort;
            DiscoveryPort = configuration.DiscoveryPort;
            DiscoveryEnabled = configuration.DiscoveryEnabled;
            ServerId = configuration.ServerId;
            CertificateFingerprint = configuration.ServerCertificateSha256;
            ManualHost = configuration.ServerHost ?? ManualHost;
            ManualServerId = configuration.ServerId ?? ManualServerId;
            ManualFingerprint = configuration.ServerCertificateSha256 ?? ManualFingerprint;
            TerminalName = configuration.TerminalName ?? TerminalName;
            TerminalRole = configuration.TerminalRole ?? TerminalRole;

            await RefreshTerminalsCoreAsync();
            StatusMessage = "Network configuration refreshed.";
        });
    }

    private async Task DiscoverAsync()
    {
        await ExecuteAsync(async () =>
        {
            DiscoveredServers.Clear();
            foreach (var server in await _discovery.DiscoverAsync(
                         TimeSpan.FromSeconds(3), DiscoveryPort))
                DiscoveredServers.Add(server);

            SelectedServer = DiscoveredServers.FirstOrDefault();
            StatusMessage = DiscoveredServers.Count == 0
                ? "No Main POS Server answered discovery. Manual host/pinning is still available."
                : DiscoveredServers.Count + " Main POS Server(s) discovered.";
        });
    }

    private async Task SaveStandaloneAsync()
    {
        if (!CanLeaveClientMode)
        {
            StatusMessage = "A Client Terminal cannot silently become authoritative. Reconfigure it through a deliberate migration/recovery workflow.";
            return;
        }

        await ExecuteAsync(async () =>
        {
            var current = await _configurationStore.LoadAsync();
            var next = current with
            {
                Mode = DeploymentMode.Standalone,
                ServerName = ServerName.Trim(),
                ServerPort = ServerPort,
                DiscoveryPort = DiscoveryPort,
                DiscoveryEnabled = DiscoveryEnabled,
                IsConfigured = true,
            };
            await _configurationStore.SaveAsync(next);
            Mode = DeploymentMode.Standalone;
            StatusMessage = "Standalone mode saved. Restart the desktop application to apply the service graph.";
        });
    }

    private async Task SaveServerAsync()
    {
        if (!CanAdministerServer)
        {
            StatusMessage = "Main Server mode can only be configured on the workstation that owns the authoritative POS database.";
            return;
        }

        await ExecuteAsync(async () =>
        {
            var terminals = _services.GetRequiredService<ILocalTerminalService>();
            var identity = await terminals.GetOrCreateServerIdentityAsync(ServerName);
            var current = await _configurationStore.LoadAsync();
            var next = current with
            {
                Mode = DeploymentMode.Server,
                ServerName = ServerName.Trim(),
                ServerPort = ServerPort,
                DiscoveryPort = DiscoveryPort,
                DiscoveryEnabled = DiscoveryEnabled,
                ServerHost = Environment.MachineName,
                ServerId = identity.ServerId,
                IsConfigured = true,
            };
            await _configurationStore.SaveAsync(next);
            ServerId = identity.ServerId;
            Mode = DeploymentMode.Server;
            StatusMessage = "Main Server mode saved. Restart BusinessOS POS and the Local Server host to apply it.";
        });
    }

    private async Task IssuePairingCodeAsync()
    {
        await ExecuteAsync(async () =>
        {
            var terminals = _services.GetService<ILocalTerminalService>()
                ?? throw new InvalidOperationException("Pairing codes can be issued only on the Main POS Server.");
            var issued = await terminals.CreatePairingCodeAsync(TimeSpan.FromMinutes(5));
            IssuedPairingCode = issued.Code;
            PairingExpiry = issued.ExpiresAt.ToLocalTime().ToString("g");
            StatusMessage = "One-time pairing code issued. It expires in five minutes.";
        });
    }

    private async Task PairAsync()
    {
        await ExecuteAsync(async () =>
        {
            var server = SelectedServer ?? BuildManualAdvertisement();
            var paired = await _pairing.PairAsync(
                server,
                PairingCode,
                TerminalName,
                TerminalRole);
            Mode = paired.Mode;
            ServerId = paired.ServerId;
            CertificateFingerprint = paired.ServerCertificateSha256;
            StatusMessage = "Terminal paired securely. Restart BusinessOS POS to enter Client mode.";
        });
    }

    private async Task TestConnectionAsync()
    {
        await ExecuteAsync(async () =>
        {
            var result = await _pairing.TestConnectionAsync();
            ConnectionStatus = result.IsConnected
                ? "Connected · " + result.Latency?.TotalMilliseconds.ToString("N0") + " ms"
                : "Disconnected · " + result.Message;
            StatusMessage = ConnectionStatus;
        });
    }

    private async Task RevokeTerminalAsync()
    {
        if (SelectedTerminal is null) return;
        await ExecuteAsync(async () =>
        {
            var terminals = _services.GetService<ILocalTerminalService>()
                ?? throw new InvalidOperationException("Terminal administration is available only on the Main POS Server.");
            await terminals.RevokeAsync(SelectedTerminal.TerminalId);
            await RefreshTerminalsCoreAsync();
            StatusMessage = "Terminal revoked. Its active LAN sessions were invalidated.";
        });
    }

    private async Task ReactivateTerminalAsync()
    {
        if (SelectedTerminal is null) return;
        await ExecuteAsync(async () =>
        {
            var terminals = _services.GetService<ILocalTerminalService>()
                ?? throw new InvalidOperationException("Terminal administration is available only on the Main POS Server.");
            await terminals.ReactivateAsync(SelectedTerminal.TerminalId);
            await RefreshTerminalsCoreAsync();
            StatusMessage = "Terminal reactivated. The workstation must still possess its protected pairing secret.";
        });
    }

    private async Task RefreshTerminalsCoreAsync()
    {
        RegisteredTerminals.Clear();
        var terminals = _services.GetService<ILocalTerminalService>();
        if (terminals is null) return;

        foreach (var terminal in await terminals.ListAsync())
            RegisteredTerminals.Add(terminal);
    }

    private LocalServerDiscoveryAdvertisement BuildManualAdvertisement()
    {
        if (string.IsNullOrWhiteSpace(ManualHost) ||
            string.IsNullOrWhiteSpace(ManualServerId) ||
            string.IsNullOrWhiteSpace(ManualFingerprint))
            throw new InvalidOperationException(
                "Choose a discovered server or enter host, server ID and certificate fingerprint manually.");

        return new LocalServerDiscoveryAdvertisement(
            "BusinessOS.POS.LocalServer",
            "v1",
            ManualServerId.Trim(),
            "Manual Main POS Server",
            ManualHost.Trim(),
            ServerPort,
            NetworkConfiguration.NormalizeFingerprint(ManualFingerprint));
    }

    private async Task ExecuteAsync(Func<Task> action)
    {
        if (IsBusy) return;
        IsBusy = true;
        try { await action(); }
        catch (Exception ex) { StatusMessage = ex.Message; }
        finally { IsBusy = false; }
    }
}
