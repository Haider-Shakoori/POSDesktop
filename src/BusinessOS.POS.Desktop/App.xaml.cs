using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using BusinessOS.POS.Application.Abstractions.Authentication;
using BusinessOS.POS.Application.Abstractions.Networking;
using BusinessOS.POS.Application.Abstractions.Persistence;
using BusinessOS.POS.Application.Abstractions.Storage;
using BusinessOS.POS.Application.Abstractions.Updates;
using BusinessOS.POS.Desktop.Authentication;
using BusinessOS.POS.Desktop.Catalog;
using BusinessOS.POS.Desktop.Cash;
using BusinessOS.POS.Desktop.Closing;
using BusinessOS.POS.Desktop.Customers;
using BusinessOS.POS.Desktop.Dashboard;
using BusinessOS.POS.Desktop.Expenses;
using BusinessOS.POS.Desktop.Inventory;
using BusinessOS.POS.Desktop.Pos;
using BusinessOS.POS.Desktop.Purchasing;
using BusinessOS.POS.Desktop.Reports;
using BusinessOS.POS.Desktop.Sales;
using BusinessOS.POS.Desktop.Settings;
using BusinessOS.POS.Desktop.Terminals;
using BusinessOS.POS.Infrastructure;
using BusinessOS.POS.Infrastructure.Networking;
using BusinessOS.POS.Infrastructure.Updates;
using BusinessOS.POS.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BusinessOS.POS.Desktop;

public partial class App : System.Windows.Application
{
    private IHost? _host;
    private Process? _localServerProcess;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;

        try
        {
            if (TryHandleInstallVerification(e.Args))
            {
                Shutdown(0);
                return;
            }

            var paths = new ApplicationPaths();
            paths.EnsureCreated();

            var networkStore = new NetworkConfigurationStore(paths);
            await ApplyDeploymentDefaultAsync(networkStore);

            _host = Host.CreateDefaultBuilder()
                .ConfigureServices(services =>
                {
                    services.AddSingleton<IApplicationPaths>(paths);
                    services.AddSingleton<INetworkConfigurationStore>(networkStore);
                    services.AddSingleton<INetworkSecretStore, WindowsNetworkSecretStore>();
                    services.AddSingleton<ILanTerminalRegistry, FileLanTerminalRegistry>();
                    services.AddSingleton<ILanClientService, LanClientService>();
                    services.AddSingleton<IPosUpdateService>(_ =>
                    {
                        var options = new UpdateOptions(
                            Environment.GetEnvironmentVariable("BUSINESSOS_POS_UPDATE_MANIFEST_URL") ?? string.Empty,
                            Environment.GetEnvironmentVariable("BUSINESSOS_POS_UPDATE_PUBLIC_KEY_PEM") ?? string.Empty);
                        return new PosUpdateService(new HttpClient(), options, paths.UpdatesDirectory);
                    });

                    services.AddBusinessOSPosPersistence();
                    services.AddTransient<LoginViewModel>();
                    services.AddTransient<OwnerSetupViewModel>();
                    services.AddTransient<PosViewModel>();
                    services.AddTransient<ProductCatalogViewModel>();
                    services.AddTransient<InventoryViewModel>();
                    services.AddTransient<SalesViewModel>();
                    services.AddTransient<CustomersViewModel>();
                    services.AddTransient<PurchasingViewModel>();
                    services.AddTransient<CashViewModel>();
                    services.AddTransient<DailyClosingViewModel>();
                    services.AddTransient<DashboardViewModel>();
                    services.AddTransient<ReportsViewModel>();
                    services.AddTransient<TerminalsViewModel>();
                    services.AddTransient<SettingsViewModel>();
                    services.AddTransient<ExpensesViewModel>();
                    services.AddSingleton<IReceiptPrintService, WpfReceiptPrintService>();
                    services.AddTransient<MainWindowViewModel>();
                })
                .Build();

            await _host.StartAsync();

            var initializer = _host.Services.GetRequiredService<ILocalDatabaseInitializer>();
            await initializer.InitializeAsync();

            await StartBundledLocalServerIfRequiredAsync(networkStore);

            var bootstrap = _host.Services.GetRequiredService<IOwnerBootstrapService>();
            if (!await bootstrap.HasAnyUsersAsync())
            {
                var setup = new OwnerSetupWindow(
                    _host.Services.GetRequiredService<OwnerSetupViewModel>());

                if (setup.ShowDialog() != true)
                {
                    Shutdown();
                    return;
                }
            }

            await RunSessionLoopAsync();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                "BusinessOS POS could not start.\n\n" + exception.Message,
                "Startup error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
        }
    }

    private async Task RunSessionLoopAsync()
    {
        if (_host is null)
            return;

        while (true)
        {
            var login = new LoginWindow(_host.Services.GetRequiredService<LoginViewModel>());
            if (login.ShowDialog() != true)
            {
                Shutdown();
                return;
            }

            var main = new MainWindow(_host.Services.GetRequiredService<MainWindowViewModel>());
            var signedOut = false;
            main.SignOutRequested += (_, _) => signedOut = true;
            main.ShowDialog();

            if (!signedOut)
            {
                Shutdown();
                return;
            }

            await Task.Yield();
        }
    }

    private static async Task ApplyDeploymentDefaultAsync(INetworkConfigurationStore store)
    {
        var markerPath = Path.Combine(AppContext.BaseDirectory, "deployment-default.txt");
        if (!File.Exists(markerPath))
            return;

        var existing = await store.LoadAsync();
        if (existing.IsConfigured || existing.Mode != DeploymentMode.Standalone)
            return;

        var marker = (await File.ReadAllTextAsync(markerPath)).Trim();
        if (!Enum.TryParse<DeploymentMode>(marker, ignoreCase: true, out var mode))
            return;

        await store.SaveAsync(existing with
        {
            Mode = mode,
            IsConfigured = mode != DeploymentMode.Client,
            ServerName = mode == DeploymentMode.Server ? "Main POS Server" : existing.ServerName,
        });
    }

    private async Task StartBundledLocalServerIfRequiredAsync(INetworkConfigurationStore store)
    {
        var configuration = await store.LoadAsync();
        if (configuration.Mode != DeploymentMode.Server)
            return;

        var serverExe = Path.Combine(
            AppContext.BaseDirectory,
            "Server",
            "BusinessOS.POS.LocalServer.exe");

        if (!File.Exists(serverExe))
            return;

        _localServerProcess = Process.Start(new ProcessStartInfo(serverExe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(serverExe)!,
        });
    }

    private static bool TryHandleInstallVerification(string[] args)
    {
        var verifyArg = args.FirstOrDefault(x =>
            x.StartsWith("--verify-install=", StringComparison.OrdinalIgnoreCase));

        if (verifyArg is null)
            return false;

        var outputPath = verifyArg["--verify-install=".Length..].Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new InvalidOperationException("Install verification output path is missing.");

        var markerPath = Path.Combine(AppContext.BaseDirectory, "deployment-default.txt");
        var deployment = File.Exists(markerPath)
            ? File.ReadAllText(markerPath).Trim()
            : DeploymentMode.Standalone.ToString();

        var payload = new
        {
            product = "BusinessOS POS",
            version = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.1.0",
            deployment_hint = deployment,
            executable = Environment.ProcessPath,
        };

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        File.WriteAllText(
            outputPath,
            JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));

        return true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            if (_localServerProcess is { HasExited: false })
                _localServerProcess.Kill(entireProcessTree: true);
        }
        catch
        {
        }
        finally
        {
            _localServerProcess?.Dispose();
        }

        if (_host is not null)
        {
            _host.StopAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            _host.Dispose();
        }

        base.OnExit(e);
    }
}
