using System.Text.Json;
using BusinessOS.POS.Application.Abstractions.Authentication;
using BusinessOS.POS.Application.Abstractions.Networking;

namespace BusinessOS.POS.LocalClient;

public sealed class LanApiClient(PinnedLocalServerTransport transport)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<T> PostAsync<T>(
        string path, object payload, bool terminal = true, bool session = true,
        CancellationToken cancellationToken = default)
    {
        using var response = await transport.SendAsync(
            HttpMethod.Post, path, payload, terminal, session, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return (await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken))
            ?? throw new InvalidOperationException("The POS server returned an empty response.");
    }

    public async Task PostAsync(
        string path, object payload, bool terminal = true, bool session = true,
        CancellationToken cancellationToken = default)
    {
        using var response = await transport.SendAsync(
            HttpMethod.Post, path, payload, terminal, session, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<T> GetAsync<T>(
        string path, bool terminal = false, bool session = false,
        CancellationToken cancellationToken = default)
    {
        using var response = await transport.SendAsync(
            HttpMethod.Get, path, null, terminal, session, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return (await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken))
            ?? throw new InvalidOperationException("The POS server returned an empty response.");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        string message;
        try
        {
            using var doc = JsonDocument.Parse(text);
            message = doc.RootElement.TryGetProperty("message", out var value)
                ? value.GetString() ?? response.ReasonPhrase ?? "POS server request failed."
                : response.ReasonPhrase ?? "POS server request failed.";
        }
        catch { message = string.IsNullOrWhiteSpace(text) ? "POS server request failed." : text; }

        if ((int)response.StatusCode == 403) throw new PermissionDeniedException("remote");
        if ((int)response.StatusCode == 401) throw new UnauthorizedAccessException(message);
        throw new InvalidOperationException(message);
    }
}
