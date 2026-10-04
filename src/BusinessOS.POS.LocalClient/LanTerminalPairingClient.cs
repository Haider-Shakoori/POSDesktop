using System.Text;
using System.Text.Json;
using BusinessOS.POS.Application.Abstractions.Networking;

namespace BusinessOS.POS.LocalClient;

public sealed class LanTerminalPairingClient(
    INetworkConfigurationStore configurationStore,
    INetworkSecretStore secrets)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<PairTerminalResult> PairAsync(
        LocalServerDiscoveryAdvertisement server,
        string pairingCode,
        string terminalName,
        string terminalRole,
        CancellationToken cancellationToken = default)
    {
        var current = await configurationStore.LoadAsync(cancellationToken);
        var terminalId = string.IsNullOrWhiteSpace(current.TerminalId)
            ? Guid.CreateVersion7().ToString()
            : current.TerminalId;

        using var handler = PinnedLocalServerTransport.CreatePinnedHandler(server.CertificateSha256);
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        var request = new PairTerminalRequest(
            pairingCode,
            terminalId,
            terminalName.Trim(),
            Environment.MachineName,
            terminalRole.Trim());
        using var content = new StringContent(
            JsonSerializer.Serialize(request, JsonOptions), Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(
            $"https://{server.HostName}:{server.Port}/api/local/v1/pairing/complete",
            content,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await response.Content.ReadAsStringAsync(cancellationToken));

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var paired = await JsonSerializer.DeserializeAsync<PairTerminalResult>(stream, JsonOptions, cancellationToken)
                     ?? throw new InvalidOperationException("The POS server returned an empty pairing response.");
        if (!string.Equals(paired.ServerId, server.ServerId, StringComparison.Ordinal))
            throw new InvalidOperationException("The paired server identity changed during pairing.");

        await secrets.SetAsync("terminal-secret:" + paired.TerminalId, paired.TerminalSecret, cancellationToken);
        await configurationStore.SaveAsync(current with
        {
            Mode = DeploymentMode.Client,
            ServerHost = server.HostName,
            ServerPort = server.Port,
            ServerId = server.ServerId,
            ServerCertificateSha256 = server.CertificateSha256,
            TerminalId = paired.TerminalId,
            TerminalName = terminalName.Trim(),
            TerminalRole = terminalRole.Trim(),
            IsConfigured = true,
        }, cancellationToken);

        return paired;
    }
}
