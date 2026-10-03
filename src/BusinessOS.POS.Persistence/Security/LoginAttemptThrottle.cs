using System.Collections.Concurrent;

namespace BusinessOS.POS.Persistence.Security;

public sealed class LoginAttemptThrottle
{
    private const int MaximumFailures = 5;
    private static readonly TimeSpan LockDuration = TimeSpan.FromSeconds(60);
    private readonly ConcurrentDictionary<string, AttemptState> _states = new(StringComparer.Ordinal);

    public bool CanAttempt(string normalizedUsername, out int secondsRemaining)
    {
        secondsRemaining = 0;

        if (!_states.TryGetValue(normalizedUsername, out var state) || state.LockedUntil is null)
        {
            return true;
        }

        var remaining = state.LockedUntil.Value - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            _states.TryRemove(normalizedUsername, out _);
            return true;
        }

        secondsRemaining = Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds));
        return false;
    }

    public void RegisterFailure(string normalizedUsername)
    {
        _states.AddOrUpdate(
            normalizedUsername,
            _ => new AttemptState(1, null),
            (_, current) =>
            {
                var failures = current.Failures + 1;
                return failures >= MaximumFailures
                    ? new AttemptState(failures, DateTimeOffset.UtcNow.Add(LockDuration))
                    : new AttemptState(failures, current.LockedUntil);
            });
    }

    public void Clear(string normalizedUsername) => _states.TryRemove(normalizedUsername, out _);

    private sealed record AttemptState(int Failures, DateTimeOffset? LockedUntil);
}
