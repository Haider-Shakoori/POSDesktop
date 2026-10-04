using System.Net.Http.Json;
using BusinessOS.POS.Application.Abstractions.Authentication;
using BusinessOS.POS.Domain.Authentication;

namespace BusinessOS.POS.LocalClient;

public sealed class LanUserSessionService(
    PinnedLocalServerTransport transport,
    LanApiRequestFactory requestFactory,
    LanClientSessionState state)
    : IUserSessionService
{
    public UserSessionSnapshot? Current => state.Get()?.User;

    public async Task<UserSessionSnapshot> LoginAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        var connection = await transport.GetPairedConnectionAsync(cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Post, "auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(username.Trim(), password)),
        };
        request.Headers.TryAddWithoutValidation("X-BusinessOS-Terminal-Id", connection.Pairing.TerminalId);
        request.Headers.TryAddWithoutValidation("X-BusinessOS-Terminal-Secret", connection.Pairing.TerminalSecret);

        using var response = await connection.Client.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        var body = await response.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken: cancellationToken);
        if (!response.IsSuccessStatusCode || body is null)
            throw new InvalidOperationException(body?.Message ?? "The Main POS Server rejected the login.");

        state.Set(body.AccessToken, body.ExpiresAt, body.User);
        return body.User;
    }

    public async Task UpdatePreferredLocaleAsync(
        string locale,
        CancellationToken cancellationToken = default)
    {
        var current = state.Get() ?? throw new InvalidOperationException("No POS user is signed in.");
        var (client, request) = await requestFactory.CreateAsync(
            HttpMethod.Post, "auth/locale/" + Uri.EscapeDataString(locale), cancellationToken);
        using (request)
        using (var response = await client.SendAsync(request, cancellationToken))
            response.EnsureSuccessStatusCode();

        state.UpdateUser(current.User with { PreferredLocale = locale });
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        if (state.Get() is null) return;
        try
        {
            var (client, request) = await requestFactory.CreateAsync(
                HttpMethod.Post, "auth/logout", cancellationToken);
            using (request)
            using (var response = await client.SendAsync(request, cancellationToken))
            {
                // Local sign-out still completes if the server becomes unavailable.
            }
        }
        finally { state.Clear(); }
    }

    private sealed record LoginRequest(string Username, string Password);
    private sealed record LoginResponse(
        string AccessToken,
        DateTimeOffset ExpiresAt,
        UserSessionSnapshot User,
        string? Message = null);
}
