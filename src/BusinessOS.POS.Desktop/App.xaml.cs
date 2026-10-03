using System.Windows;
using BusinessOS.POS.Application.Abstractions.Authentication;
using BusinessOS.POS.Application.Abstractions.Persistence;
using BusinessOS.POS.Application.Abstractions.Storage;
using BusinessOS.POS.Desktop.Authentication;
using BusinessOS.POS.Desktop.Catalog;
using BusinessOS.POS.Desktop.Inventory;
using BusinessOS.POS.Desktop.Sales;
using BusinessOS.POS.Desktop.Pos;
using BusinessOS.POS.Infrastructure;
using BusinessOS.POS.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BusinessOS.POS.Desktop;

public partial class App : System.Windows.Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;

        try
        {
            _host = Host.CreateDefaultBuilder()
                .ConfigureServices(services =>
                {
                    services.AddSingleton<IApplicationPaths, ApplicationPaths>();
                    services.AddBusinessOSPosPersistence();
                    services.AddTransient<LoginViewModel>();
                    services.AddTransient<OwnerSetupViewModel>();
                    services.AddTransient<PosViewModel>();
                    services.AddTransient<ProductCatalogViewModel>();
                    services.AddTransient<InventoryViewModel>();
                    services.AddTransient<SalesViewModel>();
                    services.AddSingleton<IReceiptPrintService, WpfReceiptPrintService>();
                    services.AddTransient<MainWindowViewModel>();
                })
                .Build();

            await _host.StartAsync();

            var initializer = _host.Services.GetRequiredService<ILocalDatabaseInitializer>();
            await initializer.InitializeAsync();

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
        {
            return;
        }

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

    protected override void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            _host.StopAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            _host.Dispose();
        }

        base.OnExit(e);
    }
}
