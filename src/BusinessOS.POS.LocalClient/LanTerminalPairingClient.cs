using System.Net.Http.Json;
using BusinessOS.POS.Application.Abstractions.Networking;

namespace BusinessOS.POS.LocalClient;

public sealed class LanTerminalPairingClient(
    INetworkConfigurationStore configurationStore,
    INetworkSecretStore secretStore)
{
    public async Task<NetworkConfiguration> PairAsync(
        LocalServerDiscoveryAdvertisement server,
        string pairingCode,
        string terminalName,
        string terminalRole,
        CancellationToken cancellationToken = default)
    {
        var existing = await configurationStore.LoadAsync(cancellationToken);
        var terminalId = !string.IsNullOrWhiteSpace(existing.TerminalId) &&
                         Guid.TryParse(existing.TerminalId, out var parsed)
            ? parsed.ToString()
            : Guid.CreateVersion7().ToString();

        using var client = PinnedLocalServerTransport.CreatePinnedClient(
            server.HostName, server.Port, server.CertificateSha256);

        using var response = await client.PostAsJsonAsync(
            "pairing/complete",
            new PairRequest(
                pairingCode.Trim(),
                terminalId,
                terminalName.Trim(),
                Environment.MachineName,
                terminalRole.Trim()),
            cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<PairTerminalResult>(
            cancellationToken: cancellationToken);
        if (!response.IsSuccessStatusCode || result is null)
            throw new InvalidOperationException(
                $"Terminal pairing failed with HTTP {(int)response.StatusCode}.");

        if (!string.Equals(result.ServerId, server.ServerId, StringComparison.Ordinal))
            throw new InvalidOperationException("The pairing response came from a different POS server.");

        var configuration = existing with
        {
            Mode = DeploymentMode.Client,
            ServerHost = server.HostName,
            ServerPort = server.Port,
            ServerId = result.ServerId,
            ServerCertificateSha256 = NetworkConfiguration.NormalizeFingerprint(server.CertificateSha256),
            TerminalId = result.TerminalId,
            TerminalName = terminalName.Trim(),
            TerminalRole = terminalRole.Trim(),
            DiscoveryEnabled = true,
            IsConfigured = true,
        };

        await secretStore.SaveTerminalPairingAsync(
            new TerminalPairingSecret(
                result.TerminalId,
                result.TerminalSecret,
                result.ServerId,
                configuration.ServerCertificateSha256!),
            cancellationToken);
        await configurationStore.SaveAsync(configuration, cancellationToken);
        return configuration;
    }

    public async Task<LocalServerConnectionStatus> TestConnectionAsync(
        CancellationToken cancellationToken = default)
    {
        var configuration = await configurationStore.LoadAsync(cancellationToken);
        if (configuration.Mode != DeploymentMode.Client || !configuration.IsConfigured)
            return new LocalServerConnectionStatus(false, null, null, null, null, "Client Terminal is not paired.");

        var started = DateTimeOffset.UtcNow;
        try
        {
            using var client = PinnedLocalServerTransport.CreatePinnedClient(
                configuration.ServerHost!,
                configuration.ServerPort,
                configuration.ServerCertificateSha256!);
            var info = await client.GetFromJsonAsync<ServerInfoResponse>(
                "server-info", cancellationToken)
                ?? throw new InvalidOperationException("The Main POS Server returned no identity.");

            if (!string.Equals(info.ServerId, configuration.ServerId, StringComparison.Ordinal))
                throw new InvalidOperationException("The configured host now resolves to a different POS server.");

            return new LocalServerConnectionStatus(
                true, info.ServerId, info.ServerName,
                DateTimeOffset.UtcNow - started, DateTimeOffset.UtcNow, "Connected");
        }
        catch (Exception ex)
        {
            return new LocalServerConnectionStatus(
                false, configuration.ServerId, configuration.ServerName,
                null, null, ex.Message);
        }
    }

    private sealed record PairRequest(
        string PairingCode,
        string TerminalId,
        string Name,
        string ComputerName,
        string TerminalRole);

    private sealed record ServerInfoResponse(
        string Service,
        string ApiVersion,
        string ApplicationVersion,
        string MinimumClientVersion,
        string ServerId,
        string ServerName,
        string HostName,
        int Port,
        string CertificateSha256);
}
