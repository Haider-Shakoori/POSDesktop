using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using BusinessOS.POS.Application.Abstractions.Networking;

namespace BusinessOS.POS.LocalClient;

public sealed class PinnedLocalServerTransport(
    INetworkConfigurationStore configurationStore,
    INetworkSecretStore secretStore)
    : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private HttpClient? _client;
    private string? _signature;

    public async Task<PairedConnection> GetPairedConnectionAsync(
        CancellationToken cancellationToken = default)
    {
        var configuration = await configurationStore.LoadAsync(cancellationToken);
        configuration.Validate();

        if (configuration.Mode != DeploymentMode.Client || !configuration.IsConfigured)
            throw new InvalidOperationException("This workstation is not configured as a Client Terminal.");

        var pairing = await secretStore.LoadTerminalPairingAsync(cancellationToken)
            ?? throw new InvalidOperationException("The Client Terminal pairing secret is missing.");

        if (!string.Equals(pairing.TerminalId, configuration.TerminalId, StringComparison.Ordinal) ||
            !string.Equals(pairing.ServerId, configuration.ServerId, StringComparison.Ordinal) ||
            !string.Equals(
                NetworkConfiguration.NormalizeFingerprint(pairing.ServerCertificateSha256),
                NetworkConfiguration.NormalizeFingerprint(configuration.ServerCertificateSha256!),
                StringComparison.Ordinal))
            throw new InvalidOperationException("The protected terminal pairing does not match network configuration.");

        var signature = configuration.ServerHost + "|" + configuration.ServerPort + "|" +
                        NetworkConfiguration.NormalizeFingerprint(configuration.ServerCertificateSha256!);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_client is null || !string.Equals(_signature, signature, StringComparison.Ordinal))
            {
                _client?.Dispose();
                _client = CreatePinnedClient(
                    configuration.ServerHost!,
                    configuration.ServerPort,
                    configuration.ServerCertificateSha256!);
                _signature = signature;
            }

            return new PairedConnection(_client, pairing, configuration);
        }
        finally { _gate.Release(); }
    }

    public static HttpClient CreatePinnedClient(
        string host,
        int port,
        string certificateSha256)
    {
        var expected = NetworkConfiguration.NormalizeFingerprint(certificateSha256);
        if (expected.Length != 64)
            throw new InvalidOperationException("A valid server certificate SHA-256 fingerprint is required.");

        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(5),
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            MaxConnectionsPerServer = 32,
            AutomaticDecompression = System.Net.DecompressionMethods.Brotli |
                                     System.Net.DecompressionMethods.GZip,
            SslOptions =
            {
                RemoteCertificateValidationCallback = (_, certificate, _, _) =>
                {
                    if (certificate is null) return false;
                    using var cert = new X509Certificate2(certificate);
                    var actual = Convert.ToHexString(cert.GetCertHash(HashAlgorithmName.SHA256));
                    return CryptographicOperations.FixedTimeEquals(
                        Convert.FromHexString(expected),
                        Convert.FromHexString(actual));
                },
            },
        };

        return new HttpClient(handler, disposeHandler: true)
        {
            BaseAddress = new Uri($"https://{host}:{port}/api/local/v1/"),
            Timeout = TimeSpan.FromSeconds(30),
        };
    }

    public void Dispose()
    {
        _client?.Dispose();
        _gate.Dispose();
    }

    public sealed record PairedConnection(
        HttpClient Client,
        TerminalPairingSecret Pairing,
        NetworkConfiguration Configuration);
}
