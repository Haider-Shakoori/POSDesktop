using System.Text.Json;
using BusinessOS.POS.Application.Abstractions.Networking;
using BusinessOS.POS.Application.Abstractions.Storage;

namespace BusinessOS.POS.Infrastructure.Networking;

public sealed class NetworkConfigurationStore(IApplicationPaths paths) : INetworkConfigurationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private string ConfigurationPath => Path.Combine(paths.RootPath, "network.json");

    public async Task<NetworkConfiguration> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(ConfigurationPath))
            return new NetworkConfiguration();

        await using var stream = new FileStream(
            ConfigurationPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);

        var configuration = await JsonSerializer.DeserializeAsync<NetworkConfiguration>(
            stream, JsonOptions, cancellationToken) ?? new NetworkConfiguration();

        configuration.Validate();
        return configuration;
    }

    public async Task SaveAsync(
        NetworkConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        configuration.Validate();
        paths.EnsureCreated();

        await _writeGate.WaitAsync(cancellationToken);
        var temporary = ConfigurationPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(
                temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                16 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, configuration, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporary, ConfigurationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            _writeGate.Release();
        }
    }
}
