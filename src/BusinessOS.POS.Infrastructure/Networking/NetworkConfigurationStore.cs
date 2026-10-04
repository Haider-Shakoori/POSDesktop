using System.Text.Json;
using BusinessOS.POS.Application.Abstractions.Networking;
using BusinessOS.POS.Application.Abstractions.Storage;

namespace BusinessOS.POS.Infrastructure.Networking;

public sealed class NetworkConfigurationStore(IApplicationPaths paths) : INetworkConfigurationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly SemaphoreSlim _gate = new(1,1);

    public async Task<NetworkConfiguration> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(paths.NetworkConfigurationPath)) return new NetworkConfiguration();
        await using var stream = File.OpenRead(paths.NetworkConfigurationPath);
        var value = await JsonSerializer.DeserializeAsync<NetworkConfiguration>(stream, JsonOptions, cancellationToken) ?? new NetworkConfiguration();
        value.Validate();
        return value;
    }

    public async Task SaveAsync(NetworkConfiguration configuration, CancellationToken cancellationToken = default)
    {
        configuration.Validate(); paths.EnsureCreated();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var tmp = paths.NetworkConfigurationPath + ".tmp";
            await using (var stream = File.Create(tmp))
                await JsonSerializer.SerializeAsync(stream, configuration, JsonOptions, cancellationToken);
            File.Move(tmp, paths.NetworkConfigurationPath, true);
        }
        finally { _gate.Release(); }
    }
}
