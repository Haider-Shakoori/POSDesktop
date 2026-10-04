using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using BusinessOS.POS.Application.Abstractions.Networking;

namespace BusinessOS.POS.LocalClient;

public sealed class PinnedLocalServerTransport(
    INetworkConfigurationStore configurationStore,
    INetworkSecretStore secrets,
    LanClientSessionState session)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string relativePath,
        object? payload,
        bool requireTerminal,
        bool requireSession,
        CancellationToken cancellationToken = default)
    {
        var configuration = await configurationStore.LoadAsync(cancellationToken);
        configuration.Validate();
        if (configuration.Mode != DeploymentMode.Client || !configuration.IsConfigured)
            throw new InvalidOperationException("This workstation is not configured as a paired POS client terminal.");

        var request = new HttpRequestMessage(
            method,
            new Uri($"https://{configuration.ServerHost}:{configuration.ServerPort}/api/local/v1/{relativePath.TrimStart('/')}"));

        if (payload is not null)
        {
            request.Content = new StringContent(
                JsonSerializer.Serialize(payload, JsonOptions),
                Encoding.UTF8,
                "application/json");
        }

        if (requireTerminal)
        {
            var terminalId = configuration.TerminalId
                ?? throw new InvalidOperationException("The terminal identity is missing.");
            var secret = await secrets.GetAsync("terminal-secret:" + terminalId, cancellationToken)
                ?? throw new InvalidOperationException("The paired terminal secret is missing.");
            request.Headers.Add("X-BusinessOS-Terminal-Id", terminalId);
            request.Headers.Add("X-BusinessOS-Terminal-Secret", secret);
        }

        if (requireSession)
        {
            var token = session.SessionToken;
            if (string.IsNullOrWhiteSpace(token) || session.ExpiresAt <= DateTimeOffset.UtcNow)
                throw new UnauthorizedAccessException("The LAN user session has expired. Sign in again.");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }

        using var handler = new SocketsHttpHandler();
        var expected = NormalizeFingerprint(configuration.ServerCertificateSha256!);
        handler.SslOptions.RemoteCertificateValidationCallback =
            (_, certificate, _, _) => MatchesFingerprint(certificate, expected);

        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        return await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
    }

    public static SocketsHttpHandler CreatePinnedHandler(string certificateSha256)
    {
        var expected = NormalizeFingerprint(certificateSha256);
        var handler = new SocketsHttpHandler();
        handler.SslOptions.RemoteCertificateValidationCallback =
            (_, certificate, _, _) => MatchesFingerprint(certificate, expected);
        return handler;
    }

    private static bool MatchesFingerprint(X509Certificate? certificate, string expected)
    {
        if (certificate is null) return false;
        var actual = Convert.ToHexString(SHA256.HashData(certificate.GetRawCertData()));
        return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeFingerprint(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Replace(":", string.Empty).Replace(" ", string.Empty).Trim().ToUpperInvariant();
        if (normalized.Length != 64 || normalized.Any(ch => !Uri.IsHexDigit(ch)))
            throw new InvalidOperationException("The server certificate fingerprint must be a SHA-256 hex value.");
        return normalized;
    }
}
