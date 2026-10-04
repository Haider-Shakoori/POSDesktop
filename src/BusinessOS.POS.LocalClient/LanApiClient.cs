using System.Net;
using System.Net.Http.Json;

namespace BusinessOS.POS.LocalClient;

public sealed class LanApiClient(LanApiRequestFactory requests)
{
    public async Task<T> GetAsync<T>(string path, CancellationToken ct = default)
    {
        var (client, request) = await requests.CreateAsync(HttpMethod.Get, path, ct);
        using (request)
        using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            await EnsureSuccessAsync(response, ct);
            return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct)
                ?? throw new InvalidOperationException("The Main POS Server returned an empty response.");
        }
    }

    public async Task<T?> GetOptionalAsync<T>(string path, CancellationToken ct = default)
    {
        var (client, request) = await requests.CreateAsync(HttpMethod.Get, path, ct);
        using (request)
        using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.NoContent)
                return default;
            await EnsureSuccessAsync(response, ct);
            return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct);
        }
    }

    public async Task<TResponse> PostAsync<TRequest, TResponse>(
        string path,
        TRequest body,
        CancellationToken ct = default)
    {
        var (client, request) = await requests.CreateAsync(HttpMethod.Post, path, ct);
        using (request)
        {
            request.Content = JsonContent.Create(body);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            await EnsureSuccessAsync(response, ct);
            return await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken: ct)
                ?? throw new InvalidOperationException("The Main POS Server returned an empty response.");
        }
    }

    public async Task PostAsync<TRequest>(
        string path,
        TRequest body,
        CancellationToken ct = default)
    {
        var (client, request) = await requests.CreateAsync(HttpMethod.Post, path, ct);
        using (request)
        {
            request.Content = JsonContent.Create(body);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            await EnsureSuccessAsync(response, ct);
        }
    }

    public async Task PostAsync(string path, CancellationToken ct = default)
    {
        var (client, request) = await requests.CreateAsync(HttpMethod.Post, path, ct);
        using (request)
        using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct))
            await EnsureSuccessAsync(response, ct);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;

        string? message = null;
        try
        {
            var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(cancellationToken: ct);
            message = error?.Message;
        }
        catch { }

        throw new InvalidOperationException(
            message ?? $"Main POS Server returned HTTP {(int)response.StatusCode}.");
    }

    private sealed record ErrorResponse(string? Message);
}
