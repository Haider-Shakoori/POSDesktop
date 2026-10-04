using BusinessOS.POS.Application.Abstractions.Authentication;
using BusinessOS.POS.Application.Abstractions.Cash;
using BusinessOS.POS.Application.Abstractions.Catalog;
using BusinessOS.POS.Application.Abstractions.Closing;
using BusinessOS.POS.Application.Abstractions.Customers;
using BusinessOS.POS.Application.Abstractions.Dashboard;
using BusinessOS.POS.Application.Abstractions.Inventory;
using BusinessOS.POS.Application.Abstractions.Purchasing;
using BusinessOS.POS.Application.Abstractions.Reporting;
using BusinessOS.POS.Application.Abstractions.Sales;
using Microsoft.Extensions.DependencyInjection;

namespace BusinessOS.POS.LocalClient;

public static class DependencyInjection
{
    public static IServiceCollection AddBusinessOSPosLocalClient(this IServiceCollection services)
    {
        services.AddSingleton<LanClientSessionState>();
        services.AddSingleton<PinnedLocalServerTransport>();
        services.AddSingleton<LanApiClient>();
        services.AddSingleton<LanTerminalPairingClient>();
        services.AddSingleton<IUserSessionService, LanUserSessionService>();
        services.AddSingleton<IPermissionAuthorizer, LanClientPermissionAuthorizer>();

        services.AddTransient<IProductCatalogService>(sp => LanRpcProxy<IProductCatalogService>.Create(sp.GetRequiredService<LanApiClient>(), "catalog"));
        services.AddTransient<IInventoryService>(sp => LanRpcProxy<IInventoryService>.Create(sp.GetRequiredService<LanApiClient>(), "inventory"));
        services.AddTransient<IPosService>(sp => LanRpcProxy<IPosService>.Create(sp.GetRequiredService<LanApiClient>(), "pos"));
        services.AddTransient<ISalesService>(sp => LanRpcProxy<ISalesService>.Create(sp.GetRequiredService<LanApiClient>(), "sales"));
        services.AddTransient<ISaleReturnService>(sp => LanRpcProxy<ISaleReturnService>.Create(sp.GetRequiredService<LanApiClient>(), "returns"));
        services.AddTransient<ICustomerService>(sp => LanRpcProxy<ICustomerService>.Create(sp.GetRequiredService<LanApiClient>(), "customers"));
        services.AddTransient<IPurchasingService>(sp => LanRpcProxy<IPurchasingService>.Create(sp.GetRequiredService<LanApiClient>(), "purchasing"));
        services.AddTransient<ICashManagementService>(sp => LanRpcProxy<ICashManagementService>.Create(sp.GetRequiredService<LanApiClient>(), "cash"));
        services.AddTransient<IBusinessDayClosingService>(sp => LanRpcProxy<IBusinessDayClosingService>.Create(sp.GetRequiredService<LanApiClient>(), "closing"));
        services.AddTransient<IDashboardService>(sp => LanRpcProxy<IDashboardService>.Create(sp.GetRequiredService<LanApiClient>(), "dashboard"));
        services.AddTransient<IReportingService>(sp => LanRpcProxy<IReportingService>.Create(sp.GetRequiredService<LanApiClient>(), "reports"));
        return services;
    }
}
