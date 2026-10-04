using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using BusinessOS.POS.Application.Abstractions.Networking;
using BusinessOS.POS.Infrastructure.Networking;

namespace BusinessOS.POS.LocalServer.Discovery;

public sealed class UdpDiscoveryResponder(
    NetworkConfiguration configuration,
    LocalServerIdentity identity,
    string certificateSha256,
    ILogger<UdpDiscoveryResponder> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.DiscoveryEnabled) return;

        using var udp = new UdpClient(AddressFamily.InterNetwork);
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, configuration.DiscoveryPort));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var message = await udp.ReceiveAsync(stoppingToken);
                var text = Encoding.UTF8.GetString(message.Buffer);
                if (!string.Equals(text, UdpLocalServerDiscovery.DiscoveryRequest, StringComparison.Ordinal)) continue;

                var ad = new LocalServerDiscoveryAdvertisement(
                    "BusinessOS.POS.LocalServer", "v1", identity.ServerId, identity.ServerName,
                    Environment.MachineName, configuration.ServerPort, certificateSha256);
                var payload = JsonSerializer.SerializeToUtf8Bytes(ad, JsonOptions);
                await udp.SendAsync(payload, message.RemoteEndPoint, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "POS LAN discovery responder error.");
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }
    }
}
