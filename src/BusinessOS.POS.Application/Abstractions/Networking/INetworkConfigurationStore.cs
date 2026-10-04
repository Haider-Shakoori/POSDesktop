namespace BusinessOS.POS.Application.Abstractions.Networking;

public interface INetworkConfigurationStore
{
    Task<NetworkConfiguration> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(NetworkConfiguration configuration, CancellationToken cancellationToken = default);
}
