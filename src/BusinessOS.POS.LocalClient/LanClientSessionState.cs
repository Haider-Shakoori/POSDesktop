using BusinessOS.POS.Domain.Authentication;

namespace BusinessOS.POS.LocalClient;

public sealed class LanClientSessionState
{
    public string? SessionToken { get; private set; }
    public UserSessionSnapshot? User { get; private set; }
    public DateTimeOffset? ExpiresAt { get; private set; }

    public void Set(string token, UserSessionSnapshot user, DateTimeOffset expiresAt)
    {
        SessionToken = token;
        User = user;
        ExpiresAt = expiresAt;
    }

    public void UpdateUser(UserSessionSnapshot user) => User = user;

    public void Clear()
    {
        SessionToken = null;
        User = null;
        ExpiresAt = null;
    }
}
