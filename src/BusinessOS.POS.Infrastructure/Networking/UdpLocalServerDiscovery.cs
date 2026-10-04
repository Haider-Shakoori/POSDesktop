using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using BusinessOS.POS.Application.Abstractions.Networking;

namespace BusinessOS.POS.Infrastructure.Networking;

public sealed class UdpLocalServerDiscovery : ILocalServerDiscovery
{
    public const string DiscoveryRequest = "BUSINESSOS_POS_DISCOVER_V1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<LocalServerDiscoveryAdvertisement>> DiscoverAsync(
        int discoveryPort, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (discoveryPort is < 1024 or > 65535) throw new ArgumentOutOfRangeException(nameof(discoveryPort));
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromSeconds(10)) throw new ArgumentOutOfRangeException(nameof(timeout));

        using var udp = new UdpClient(AddressFamily.InterNetwork);
        udp.EnableBroadcast = true;
        udp.Client.ReceiveTimeout = (int)timeout.TotalMilliseconds;

        var payload = Encoding.UTF8.GetBytes(DiscoveryRequest);
        await udp.SendAsync(payload, new IPEndPoint(IPAddress.Broadcast, discoveryPort), cancellationToken);

        var results = new Dictionary<string, LocalServerDiscoveryAdvertisement>(StringComparer.Ordinal);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(timeout);

        while (!linked.IsCancellationRequested)
        {
            try
            {
                var received = await udp.ReceiveAsync(linked.Token);
                var ad = JsonSerializer.Deserialize<LocalServerDiscoveryAdvertisement>(received.Buffer, JsonOptions);
                if (ad is null || ad.Service != "BusinessOS.POS.LocalServer" || ad.ApiVersion != "v1") continue;
                results[ad.ServerId] = ad with { HostName = received.RemoteEndPoint.Address.ToString() };
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested) { break; }
            catch (SocketException) { break; }
            catch (JsonException) { }
        }

        return results.Values.OrderBy(x => x.ServerName).ToList();
    }
}
