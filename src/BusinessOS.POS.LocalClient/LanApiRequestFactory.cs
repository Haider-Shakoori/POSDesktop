using System.Net.Http.Headers;

namespace BusinessOS.POS.LocalClient;

public sealed class LanApiRequestFactory(
    PinnedLocalServerTransport transport,
    LanClientSessionState sessionState)
{
    public async Task<(HttpClient Client, HttpRequestMessage Request)> CreateAsync(
        HttpMethod method,
        string relativeUri,
        CancellationToken cancellationToken = default)
    {
        ValidateRelativeUri(relativeUri);

        var state = sessionState.Get()
            ?? throw new InvalidOperationException("A POS user must be signed in to use this Client Terminal.");

        if (DateTimeOffset.UtcNow >= state.ExpiresAt)
        {
            sessionState.Clear();
            throw new InvalidOperationException("The LAN POS user session has expired. Sign in again.");
        }

        var connection = await transport.GetPairedConnectionAsync(cancellationToken);
        var request = new HttpRequestMessage(method, relativeUri);
        request.Headers.TryAddWithoutValidation("X-BusinessOS-Terminal-Id", connection.Pairing.TerminalId);
        request.Headers.TryAddWithoutValidation("X-BusinessOS-Terminal-Secret", connection.Pairing.TerminalSecret);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", state.AccessToken);
        return (connection.Client, request);
    }

    private static void ValidateRelativeUri(string relativeUri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeUri);
        if (relativeUri.StartsWith("/", StringComparison.Ordinal) ||
            relativeUri.StartsWith("\\", StringComparison.Ordinal) ||
            relativeUri.Contains("\\", StringComparison.Ordinal) ||
            relativeUri.Contains("../", StringComparison.Ordinal) ||
            relativeUri.Contains("/..", StringComparison.Ordinal) ||
            Uri.TryCreate(relativeUri, UriKind.Absolute, out _) ||
            !Uri.TryCreate(relativeUri, UriKind.Relative, out _))
        {
            throw new InvalidOperationException("LAN API calls must use safe relative server paths.");
        }
    }
}
