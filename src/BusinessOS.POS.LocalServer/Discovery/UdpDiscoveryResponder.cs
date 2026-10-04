using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using BusinessOS.POS.Application.Abstractions.Networking;
using BusinessOS.POS.Infrastructure.Networking;
using BusinessOS.POS.LocalServer.Runtime;

namespace BusinessOS.POS.LocalServer.Discovery;

public sealed class UdpDiscoveryResponder(
    LanServerRuntimeState runtime,
    ILogger<UdpDiscoveryResponder> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var state = runtime.Require();
        if (!state.Configuration.DiscoveryEnabled) return;

        using var udp = new UdpClient(AddressFamily.InterNetwork);
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, state.Configuration.DiscoveryPort));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var message = await udp.ReceiveAsync(stoppingToken);
                var text = Encoding.UTF8.GetString(message.Buffer);
                if (!string.Equals(text, UdpLocalServerDiscovery.DiscoveryRequest, StringComparison.Ordinal)) continue;

                var ad = new LocalServerDiscoveryAdvertisement(
                    "BusinessOS.POS.LocalServer", "v1",
                    state.Identity.ServerId, state.Identity.ServerName,
                    Environment.MachineName, state.Configuration.ServerPort,
                    state.CertificateSha256);
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
