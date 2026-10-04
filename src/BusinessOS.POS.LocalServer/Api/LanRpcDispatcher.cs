using System.Reflection;
using System.Text.Json;
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

namespace BusinessOS.POS.LocalServer.Api;

public sealed class LanRpcDispatcher(IServiceProvider services)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly IReadOnlyDictionary<string, Type> Allowed = new Dictionary<string, Type>(StringComparer.Ordinal)
    {
        ["catalog"] = typeof(IProductCatalogService),
        ["inventory"] = typeof(IInventoryService),
        ["pos"] = typeof(IPosService),
        ["sales"] = typeof(ISalesService),
        ["returns"] = typeof(ISaleReturnService),
        ["customers"] = typeof(ICustomerService),
        ["purchasing"] = typeof(IPurchasingService),
        ["cash"] = typeof(ICashManagementService),
        ["closing"] = typeof(IBusinessDayClosingService),
        ["dashboard"] = typeof(IDashboardService),
        ["reports"] = typeof(IReportingService),
    };

    public async Task<object?> InvokeAsync(
        string serviceName, string methodName, LanRpcRequest request, CancellationToken cancellationToken)
    {
        if (!Allowed.TryGetValue(serviceName, out var serviceType))
            throw new InvalidOperationException("Unknown LAN RPC service.");

        var target = services.GetRequiredService(serviceType);
        var methods = serviceType.GetMethods().Where(x => x.Name == methodName).ToList();
        var method = methods.SingleOrDefault(x =>
            x.GetParameters().Count(p => p.ParameterType != typeof(CancellationToken)) == request.Arguments.Count)
            ?? throw new InvalidOperationException("Unknown LAN RPC method.");

        var parameters = method.GetParameters();
        var invokeArgs = new object?[parameters.Length];
        var payloadIndex = 0;
        for (var i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].ParameterType == typeof(CancellationToken))
            {
                invokeArgs[i] = cancellationToken;
                continue;
            }

            invokeArgs[i] = JsonSerializer.Deserialize(
                request.Arguments[payloadIndex++].GetRawText(),
                parameters[i].ParameterType,
                JsonOptions);
        }

        object? call;
        try { call = method.Invoke(target, invokeArgs); }
        catch (TargetInvocationException ex) when (ex.InnerException is not null) { throw ex.InnerException; }

        if (call is not Task task)
            throw new InvalidOperationException("LAN RPC methods must return Task.");

        await task.ConfigureAwait(false);
        if (!method.ReturnType.IsGenericType) return null;
        return method.ReturnType.GetProperty("Result")!.GetValue(task);
    }
}
