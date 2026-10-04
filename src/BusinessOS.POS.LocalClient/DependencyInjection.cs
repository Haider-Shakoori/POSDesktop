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
        services.AddSingleton<LanApiRequestFactory>();
        services.AddSingleton<LanApiClient>();
        services.AddSingleton<LanTerminalPairingClient>();

        services.AddScoped<IUserSessionService, LanUserSessionService>();
        services.AddScoped<IPermissionAuthorizer, LanClientPermissionAuthorizer>();
        services.AddScoped<IProductCatalogService, LanProductCatalogService>();
        services.AddScoped<IInventoryService, LanInventoryService>();
        services.AddScoped<IPosService, LanPosService>();
        services.AddScoped<ISalesService, LanSalesService>();
        services.AddScoped<ISaleReturnService, LanSaleReturnService>();
        services.AddScoped<ICustomerService, LanCustomerService>();
        services.AddScoped<IPurchasingService, LanPurchasingService>();
        services.AddScoped<ICashManagementService, LanCashManagementService>();
        services.AddScoped<IBusinessDayClosingService, LanBusinessDayClosingService>();
        services.AddScoped<IDashboardService, LanDashboardService>();
        services.AddScoped<IReportingService, LanReportingService>();
        return services;
    }
}
