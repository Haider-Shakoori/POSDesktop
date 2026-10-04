using System.Collections.ObjectModel;
using BusinessOS.POS.Application.Abstractions.Networking;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BusinessOS.POS.Desktop.Terminals;

public partial class TerminalsViewModel(INetworkConfigurationStore configurations, ILanTerminalRegistry registry, ILanClientService client) : ObservableObject
{
    public ObservableCollection<LanTerminal> Terminals { get; } = [];
    public DeploymentMode[] Modes { get; } = Enum.GetValues<DeploymentMode>();

    [ObservableProperty] private DeploymentMode _selectedMode;
    [ObservableProperty] private string _serverName = "Main POS Server";
    [ObservableProperty] private int _serverPort = NetworkConfiguration.DefaultServerPort;
    [ObservableProperty] private string _serverHost = "";
    [ObservableProperty] private string _terminalName = Environment.MachineName;
    [ObservableProperty] private string _pairingCode = "";
    [ObservableProperty] private string _serverId = "";
    [ObservableProperty] private string _certificateSha256 = "";
    [ObservableProperty] private string _statusMessage = "LAN settings have not been loaded yet.";
    [ObservableProperty] private LanTerminal? _selectedTerminal;

    [RelayCommand]
    public async Task LoadAsync()
    {
        var c = await configurations.LoadAsync();
        SelectedMode = c.Mode; ServerName = c.ServerName; ServerPort = c.ServerPort; ServerHost = c.ServerHost ?? "";
        TerminalName = c.TerminalName ?? Environment.MachineName; ServerId = c.ServerId ?? ""; CertificateSha256 = c.ServerCertificateSha256 ?? "";
        await RefreshTerminalsAsync();
        StatusMessage = c.Mode switch { DeploymentMode.Server => "This PC is configured as the Main POS Server.", DeploymentMode.Client => "This PC is configured as a Client Terminal.", _ => "Standalone mode: this PC uses its own local database." };
    }

    [RelayCommand]
    private async Task SaveModeAsync()
    {
        var c = new NetworkConfiguration { Mode = SelectedMode, ServerName = ServerName.Trim(), ServerPort = ServerPort, ServerHost = string.IsNullOrWhiteSpace(ServerHost) ? null : ServerHost.Trim(), ServerId = string.IsNullOrWhiteSpace(ServerId) ? null : ServerId.Trim(), ServerCertificateSha256 = string.IsNullOrWhiteSpace(CertificateSha256) ? null : CertificateSha256.Trim(), TerminalName = TerminalName.Trim(), IsConfigured = SelectedMode != DeploymentMode.Client };
        if (SelectedMode == DeploymentMode.Server) c = c with { ServerId = await registry.GetOrCreateServerIdAsync(), ServerHost = Environment.MachineName, IsConfigured = true };
        await configurations.SaveAsync(c);
        ServerId = c.ServerId ?? ""; StatusMessage = "LAN mode saved. Restart the desktop/server process for mode changes to fully apply.";
    }

    [RelayCommand]
    private async Task CreatePairingCodeAsync()
    {
        var issue = await registry.CreatePairingCodeAsync(TimeSpan.FromMinutes(10));
        PairingCode = issue.Code; StatusMessage = $"Pairing code valid until {issue.ExpiresAt.ToLocalTime():HH:mm}.";
    }

    [RelayCommand]
    private async Task PairClientAsync()
    {
        var c = await client.PairAsync(ServerHost, ServerPort, ServerId, CertificateSha256, PairingCode, TerminalName);
        SelectedMode = c.Mode; StatusMessage = "Client terminal paired successfully.";
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        var s = await client.TestConnectionAsync(); StatusMessage = s.IsConnected ? $"Connected to {s.ServerName ?? s.ServerId} in {s.Latency?.TotalMilliseconds:N0} ms." : s.Message;
    }

    [RelayCommand]
    private async Task RefreshTerminalsAsync()
    {
        Terminals.Clear(); foreach (var t in await registry.ListAsync()) Terminals.Add(t);
    }

    [RelayCommand]
    private async Task RevokeSelectedAsync()
    {
        if (SelectedTerminal is null) return; await registry.RevokeAsync(SelectedTerminal.TerminalId); await RefreshTerminalsAsync(); StatusMessage = "Terminal access revoked.";
    }
}
