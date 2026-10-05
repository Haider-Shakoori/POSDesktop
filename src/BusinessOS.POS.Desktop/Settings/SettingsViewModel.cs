using System.Collections.ObjectModel;
using BusinessOS.POS.Application.Abstractions.Backup;
using BusinessOS.POS.Application.Abstractions.Networking;
using BusinessOS.POS.Application.Abstractions.Storage;
using BusinessOS.POS.Application.Abstractions.Updates;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BusinessOS.POS.Desktop.Settings;

public partial class SettingsViewModel(
    ILocalBackupService backups,
    INetworkConfigurationStore networkConfigurations,
    IPosUpdateService updates,
    IApplicationPaths paths) : ObservableObject
{
    private PreparedUpdate? _prepared;
    private UpdateCheckResult? _updateCheck;

    public ObservableCollection<LocalBackupSnapshot> Backups { get; } = [];

    [ObservableProperty] private LocalBackupSnapshot? _selectedBackup;
    [ObservableProperty] private string _restoreConfirmation = string.Empty;
    [ObservableProperty] private bool _canManageBackups = true;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = "Settings have not been loaded yet.";
    [ObservableProperty] private string _currentVersion = GetCurrentVersion().ToString();
    [ObservableProperty] private string _availableVersion = "—";
    [ObservableProperty] private string _releaseNotes = string.Empty;
    [ObservableProperty] private string _deploymentMode = "Standalone";

    public bool CanRestore =>
        CanManageBackups &&
        SelectedBackup is not null &&
        string.Equals(RestoreConfirmation, "RESTORE", StringComparison.Ordinal);

    public bool CanDownloadUpdate =>
        _updateCheck?.Manifest is not null &&
        _updateCheck.Availability is UpdateAvailability.Available or UpdateAvailability.Required;

    public bool CanInstallUpdate => _prepared is not null;

    partial void OnSelectedBackupChanged(LocalBackupSnapshot? value) => OnPropertyChanged(nameof(CanRestore));
    partial void OnRestoreConfirmationChanged(string value) => OnPropertyChanged(nameof(CanRestore));
    partial void OnCanManageBackupsChanged(bool value) => OnPropertyChanged(nameof(CanRestore));

    [RelayCommand]
    public async Task LoadAsync()
    {
        await RunBusyAsync(async () =>
        {
            var network = await networkConfigurations.LoadAsync();
            DeploymentMode = network.Mode.ToString();
            CanManageBackups = network.Mode != Networking.DeploymentMode.Client;

            Backups.Clear();
            if (CanManageBackups)
            {
                foreach (var backup in await backups.ListAsync())
                    Backups.Add(backup);
                StatusMessage = $"Backup directory: {paths.BackupsDirectory}";
            }
            else
            {
                StatusMessage = "Backup and restore are managed on the Main POS Server for Client Terminal mode.";
            }

            CurrentVersion = GetCurrentVersion().ToString();
        });
    }

    [RelayCommand]
    private async Task CreateBackupAsync()
    {
        if (!CanManageBackups) return;

        await RunBusyAsync(async () =>
        {
            var snapshot = await backups.CreateAsync();
            await ReloadBackupsAsync();
            SelectedBackup = Backups.FirstOrDefault(x => x.FullPath == snapshot.FullPath);
            StatusMessage = $"Verified backup created: {snapshot.FileName}";
        });
    }

    [RelayCommand]
    private async Task VerifyBackupAsync()
    {
        if (!CanManageBackups || SelectedBackup is null) return;

        await RunBusyAsync(async () =>
        {
            var result = await backups.VerifyAsync(SelectedBackup.FullPath);
            StatusMessage = result.Message;
        });
    }

    [RelayCommand]
    private async Task RestoreBackupAsync()
    {
        if (!CanRestore || SelectedBackup is null) return;

        await RunBusyAsync(async () =>
        {
            var selected = SelectedBackup;
            var result = await backups.RestoreAsync(selected.FullPath);
            RestoreConfirmation = string.Empty;
            await ReloadBackupsAsync();
            StatusMessage = result.SafetyBackup is null
                ? $"Restored {result.RestoredBackup.FileName}. Restart BusinessOS POS before continuing normal work."
                : $"Restored {result.RestoredBackup.FileName}. Safety backup created as {result.SafetyBackup.FileName}. Restart BusinessOS POS before continuing normal work.";
        });
    }

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        await RunBusyAsync(async () =>
        {
            _updateCheck = await updates.CheckAsync(GetCurrentVersion());
            AvailableVersion = _updateCheck.AvailableVersion?.ToString() ?? "—";
            ReleaseNotes = _updateCheck.Manifest?.ReleaseNotes ?? string.Empty;
            StatusMessage = _updateCheck.Message;
            OnPropertyChanged(nameof(CanDownloadUpdate));
            OnPropertyChanged(nameof(CanInstallUpdate));
        });
    }

    [RelayCommand]
    private async Task DownloadUpdateAsync()
    {
        if (!CanDownloadUpdate || _updateCheck?.Manifest is null) return;

        await RunBusyAsync(async () =>
        {
            _prepared = await updates.DownloadAndStageAsync(_updateCheck.Manifest);
            StatusMessage = $"BusinessOS POS {_prepared.Manifest.Version} downloaded, signature checked, and package checksum verified.";
            OnPropertyChanged(nameof(CanInstallUpdate));
        });
    }

    [RelayCommand]
    private async Task InstallUpdateAsync()
    {
        if (_prepared is null) return;

        await RunBusyAsync(async () =>
        {
            var network = await networkConfigurations.LoadAsync();
            string? preUpdateBackup = null;

            if (network.Mode != Networking.DeploymentMode.Client)
            {
                var snapshot = await backups.CreateAsync("pre-update");
                preUpdateBackup = snapshot.FullPath;
            }

            var plan = await updates.WriteApplyPlanAsync(
                _prepared,
                AppContext.BaseDirectory,
                paths.RootPath,
                Environment.ProcessId,
                Environment.ProcessPath,
                preUpdateBackup);

            _ = updates.LaunchApplyAgent(plan);
            StatusMessage = "Verified update is starting. BusinessOS POS will close and reopen after the files are replaced.";
            System.Windows.Application.Current.Shutdown();
        });
    }

    private async Task ReloadBackupsAsync()
    {
        Backups.Clear();
        foreach (var backup in await backups.ListAsync())
            Backups.Add(backup);
    }

    private async Task RunBusyAsync(Func<Task> action)
    {
        if (IsBusy) return;
        IsBusy = true;
        try { await action(); }
        catch (Exception exception) { StatusMessage = exception.Message; }
        finally { IsBusy = false; }
    }

    private static Version GetCurrentVersion() =>
        typeof(SettingsViewModel).Assembly.GetName().Version ?? new Version(0, 1, 0);
}
