using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using BusinessOS.POS.Application.Abstractions.Networking;
using BusinessOS.POS.LocalServer.Runtime;

namespace BusinessOS.POS.LocalServer.Discovery;

public sealed class UdpDiscoveryResponder(
    LocalServerRuntimeState runtime,
    ILogger<UdpDiscoveryResponder> logger)
    : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string Probe = "BUSINESSOS-POS-DISCOVER-V1";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            LocalServerRuntimeState.State state;
            try { state = runtime.Require(); }
            catch (InvalidOperationException)
            {
                await Task.Delay(500, stoppingToken);
                continue;
            }

            if (!state.Configuration.DiscoveryEnabled)
            {
                await Task.Delay(1000, stoppingToken);
                continue;
            }

            try
            {
                using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, state.Configuration.DiscoveryPort));
                while (!stoppingToken.IsCancellationRequested)
                {
                    var result = await udp.ReceiveAsync(stoppingToken);
                    if (Encoding.UTF8.GetString(result.Buffer) != Probe) continue;

                    var advertisement = new LocalServerDiscoveryAdvertisement(
                        "BusinessOS.POS.LocalServer",
                        "v1",
                        state.Identity.ServerId,
                        state.Identity.ServerName,
                        Environment.MachineName,
                        state.Configuration.ServerPort,
                        state.CertificateSha256);

                    var bytes = JsonSerializer.SerializeToUtf8Bytes(advertisement, JsonOptions);
                    await udp.SendAsync(bytes, result.RemoteEndPoint, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "LAN discovery responder restarting after an error.");
                await Task.Delay(2000, stoppingToken);
            }
        }
    }
}
