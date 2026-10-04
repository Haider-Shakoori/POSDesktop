using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using BusinessOS.POS.Application.Abstractions.Networking;

namespace BusinessOS.POS.Infrastructure.Networking;

public sealed class UdpLocalServerDiscovery : ILocalServerDiscovery
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string Probe = "BUSINESSOS-POS-DISCOVER-V1";

    public async Task<IReadOnlyList<LocalServerDiscoveryAdvertisement>> DiscoverAsync(
        TimeSpan timeout,
        int discoveryPort = NetworkConfiguration.DefaultDiscoveryPort,
        CancellationToken cancellationToken = default)
    {
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));

        using var udp = new UdpClient(AddressFamily.InterNetwork);
        udp.EnableBroadcast = true;
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));

        var payload = Encoding.UTF8.GetBytes(Probe);
        await udp.SendAsync(payload, payload.Length, new IPEndPoint(IPAddress.Broadcast, discoveryPort));

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        var found = new Dictionary<string, LocalServerDiscoveryAdvertisement>(StringComparer.Ordinal);

        while (!deadline.IsCancellationRequested)
        {
            try
            {
                var result = await udp.ReceiveAsync(deadline.Token);
                var advertisement = JsonSerializer.Deserialize<LocalServerDiscoveryAdvertisement>(
                    result.Buffer, JsonOptions);
                if (advertisement is null ||
                    advertisement.Service != "BusinessOS.POS.LocalServer" ||
                    advertisement.ApiVersion != "v1" ||
                    string.IsNullOrWhiteSpace(advertisement.ServerId))
                    continue;

                found[advertisement.ServerId] = advertisement;
            }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested)
            {
                break;
            }
            catch (JsonException)
            {
                // Ignore unrelated UDP traffic.
            }
        }

        return found.Values.OrderBy(x => x.ServerName).ToList();
    }

    internal static bool IsDiscoveryProbe(ReadOnlySpan<byte> payload) =>
        Encoding.UTF8.GetString(payload) == Probe;
}
