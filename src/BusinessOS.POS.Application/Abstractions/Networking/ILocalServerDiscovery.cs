namespace BusinessOS.POS.Application.Abstractions.Networking;

public interface ILocalServerDiscovery
{
    Task<IReadOnlyList<LocalServerDiscoveryAdvertisement>> DiscoverAsync(
        int discoveryPort,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}
