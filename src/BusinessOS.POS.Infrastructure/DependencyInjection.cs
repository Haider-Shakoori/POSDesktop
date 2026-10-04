using BusinessOS.POS.Application.Abstractions.Networking;
using BusinessOS.POS.Infrastructure.Networking;
using Microsoft.Extensions.DependencyInjection;

namespace BusinessOS.POS.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddBusinessOSPosInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<INetworkConfigurationStore, NetworkConfigurationStore>();
        services.AddSingleton<INetworkSecretStore, WindowsNetworkSecretStore>();
        services.AddSingleton<ILocalServerDiscovery, UdpLocalServerDiscovery>();
        return services;
    }
}
