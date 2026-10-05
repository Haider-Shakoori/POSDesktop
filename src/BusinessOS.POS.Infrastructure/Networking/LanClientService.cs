using System.Diagnostics;
using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;
using BusinessOS.POS.Application.Abstractions.Networking;

namespace BusinessOS.POS.Infrastructure.Networking;

public sealed class LanClientService(INetworkConfigurationStore configurations, INetworkSecretStore secrets) : ILanClientService
{
    public async Task<NetworkConfiguration> PairAsync(string host, int port, string serverId, string certificateSha256, string pairingCode, string terminalName, CancellationToken cancellationToken = default)
    {
        var terminalId = Guid.CreateVersion7().ToString("N");
        using var client = CreateClient(host, port, certificateSha256);
        using var response = await client.PostAsJsonAsync("pairing/complete", new PairRequest(pairingCode, terminalId, terminalName, Environment.MachineName), cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<PairResponse>(cancellationToken: cancellationToken);
        if (!response.IsSuccessStatusCode || body is null) throw new InvalidOperationException(body?.Message ?? $"Pairing failed with HTTP {(int)response.StatusCode}.");
        if (!string.Equals(body.ServerId, serverId, StringComparison.Ordinal)) throw new InvalidOperationException("The pairing response came from a different POS server.");

        var config = new NetworkConfiguration
        {
            Mode = DeploymentMode.Client, ServerHost = host.Trim(), ServerPort = port, ServerId = body.ServerId,
            ServerCertificateSha256 = Normalize(certificateSha256), TerminalId = body.TerminalId, TerminalName = terminalName.Trim(), IsConfigured = true
        };
        await secrets.SaveTerminalPairingAsync(new TerminalPairingSecret(body.TerminalId, body.TerminalSecret, body.ServerId, config.ServerCertificateSha256), cancellationToken);
        await configurations.SaveAsync(config, cancellationToken);
        return config;
    }

    public async Task<LocalServerConnectionStatus> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        var config = await configurations.LoadAsync(cancellationToken);
        if (config.Mode != DeploymentMode.Client || !config.IsConfigured || string.IsNullOrWhiteSpace(config.ServerHost) || string.IsNullOrWhiteSpace(config.ServerCertificateSha256))
            return new(false, config.ServerId, null, null, null, "Client terminal is not paired.");

        var sw = Stopwatch.StartNew();
        try
        {
            using var client = CreateClient(config.ServerHost, config.ServerPort, config.ServerCertificateSha256);
            using var response = await client.GetAsync("health", cancellationToken);
            response.EnsureSuccessStatusCode();
            var health = await response.Content.ReadFromJsonAsync<Health>(cancellationToken: cancellationToken) ?? throw new InvalidOperationException("Invalid health response.");
            return new(true, health.ServerId, health.ServerName, sw.Elapsed, DateTimeOffset.UtcNow, "Connected");
        }
        catch (Exception ex) { return new(false, config.ServerId, null, null, null, ex.Message); }
    }

    private static HttpClient CreateClient(string host, int port, string fingerprint)
    {
        var normalized = Normalize(fingerprint);
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, cert, _, _) =>
            {
                if (cert is null) return false;
                using var x = new X509Certificate2(cert);
                return string.Equals(Convert.ToHexString(x.GetCertHash(System.Security.Cryptography.HashAlgorithmName.SHA256)), normalized, StringComparison.OrdinalIgnoreCase);
            }
        };
        return new HttpClient(handler) { BaseAddress = new Uri($"https://{host}:{port}/api/local/v1/"), Timeout = TimeSpan.FromSeconds(10) };
    }

    private static string Normalize(string value) => value.Replace(":", "").Replace(" ", "").Trim().ToUpperInvariant();
    private sealed record PairRequest(string PairingCode, string TerminalId, string Name, string ComputerName);
    private sealed record PairResponse(string TerminalId, string TerminalSecret, string ServerId, DateTimeOffset RegisteredAt, string? Message = null);
    private sealed record Health(bool Available, string ServerId, string ServerName, DateTimeOffset ServerTime);
}
