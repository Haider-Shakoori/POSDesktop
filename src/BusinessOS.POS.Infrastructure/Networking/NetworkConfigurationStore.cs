using System.Text.Json;
using BusinessOS.POS.Application.Abstractions.Networking;
using BusinessOS.POS.Application.Abstractions.Storage;

namespace BusinessOS.POS.Infrastructure.Networking;

public sealed class NetworkConfigurationStore(IApplicationPaths paths) : INetworkConfigurationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string PathName => Path.Combine(paths.RootPath, "network.json");

    public async Task<NetworkConfiguration> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(PathName)) return new NetworkConfiguration();

        await using var stream = new FileStream(
            PathName, FileMode.Open, FileAccess.Read, FileShare.Read,
            16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var value = await JsonSerializer.DeserializeAsync<NetworkConfiguration>(stream, JsonOptions, cancellationToken)
                    ?? new NetworkConfiguration();
        value.Validate();
        return value;
    }

    public async Task SaveAsync(NetworkConfiguration configuration, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        configuration.Validate();
        paths.EnsureCreated();
        await _gate.WaitAsync(cancellationToken);
        var temporary = PathName + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(
                temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                16 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, configuration, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            File.Move(temporary, PathName, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            _gate.Release();
        }
    }
}
