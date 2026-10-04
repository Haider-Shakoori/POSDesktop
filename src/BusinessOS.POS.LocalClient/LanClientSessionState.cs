using BusinessOS.POS.Domain.Authentication;

namespace BusinessOS.POS.LocalClient;

public sealed class LanClientSessionState
{
    private readonly object _gate = new();
    private State? _state;

    public State? Get()
    {
        lock (_gate) return _state;
    }

    public void Set(string accessToken, DateTimeOffset expiresAt, UserSessionSnapshot user)
    {
        lock (_gate) _state = new State(accessToken, expiresAt, user);
    }

    public void UpdateUser(UserSessionSnapshot user)
    {
        lock (_gate)
        {
            if (_state is not null) _state = _state with { User = user };
        }
    }

    public void Clear()
    {
        lock (_gate) _state = null;
    }

    public sealed record State(
        string AccessToken,
        DateTimeOffset ExpiresAt,
        UserSessionSnapshot User);
}
