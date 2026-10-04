namespace BusinessOS.POS.Application.Abstractions.Networking;

public interface ILocalServerDiscovery
{
    Task<IReadOnlyList<LocalServerDiscoveryAdvertisement>> DiscoverAsync(
        TimeSpan timeout,
        int discoveryPort = NetworkConfiguration.DefaultDiscoveryPort,
        CancellationToken cancellationToken = default);
}
