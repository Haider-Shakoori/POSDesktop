using BusinessOS.POS.Application.Abstractions.Authentication;
using BusinessOS.POS.Application.Abstractions.Networking;
using BusinessOS.POS.Domain.Authentication;

namespace BusinessOS.POS.LocalClient;

public sealed class LanUserSessionService(
    LanApiClient api,
    LanClientSessionState state) : IUserSessionService
{
    public UserSessionSnapshot? Current => state.User;

    public async Task<UserSessionSnapshot> LoginAsync(
        string username, string password, CancellationToken cancellationToken = default)
    {
        var result = await api.PostAsync<LocalLoginResult>(
            "auth/login",
            new { username, password },
            terminal: true,
            session: false,
            cancellationToken);
        state.Set(result.SessionToken, result.User, result.ExpiresAt);
        return result.User;
    }

    public async Task UpdatePreferredLocaleAsync(string locale, CancellationToken cancellationToken = default)
    {
        await api.PostAsync("auth/locale", new { locale }, true, true, cancellationToken);
        if (state.User is { } current) state.UpdateUser(current with { PreferredLocale = locale });
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (state.SessionToken is not null)
                await api.PostAsync("auth/logout", new { }, true, true, cancellationToken);
        }
        finally { state.Clear(); }
    }
}
