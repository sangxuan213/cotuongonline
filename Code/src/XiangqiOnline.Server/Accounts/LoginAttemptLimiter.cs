namespace XiangqiOnline.Server.Accounts;

/// <summary>
/// Process-wide protection for expensive password verification. Limits are tracked by both
/// remote address and normalized email so reconnecting does not reset a brute-force attempt.
/// </summary>
public sealed class LoginAttemptLimiter
{
    private const int EmailFailureLimit = 5;
    private const int AddressFailureLimit = 20;
    private static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan BaseLockDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaximumLockDuration = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan StateRetention = TimeSpan.FromHours(1);
    private readonly object _gate = new();
    private readonly Dictionary<string, AttemptState> _states = new(StringComparer.OrdinalIgnoreCase);

    public bool CanAttempt(string remoteAddress, string email, DateTimeOffset now, out TimeSpan retryAfter)
    {
        lock (_gate)
        {
            Prune(now);
            var addressState = Find(AddressKey(remoteAddress));
            var emailState = Find(EmailKey(email));
            var lockedUntil = new[] { addressState?.LockedUntilUtc, emailState?.LockedUntilUtc }
                .Where(value => value.HasValue && value.Value > now)
                .Select(value => value!.Value)
                .DefaultIfEmpty(now)
                .Max();
            retryAfter = lockedUntil > now ? lockedUntil - now : TimeSpan.Zero;
            return retryAfter == TimeSpan.Zero;
        }
    }

    public void RecordFailure(string remoteAddress, string email, DateTimeOffset now)
    {
        lock (_gate)
        {
            Record(AddressKey(remoteAddress), AddressFailureLimit, now);
            Record(EmailKey(email), EmailFailureLimit, now);
        }
    }

    public void RecordSuccess(string remoteAddress, string email, DateTimeOffset now)
    {
        lock (_gate)
        {
            _states.Remove(EmailKey(email));
            if (_states.TryGetValue(AddressKey(remoteAddress), out var addressState))
            {
                addressState.Failures = Math.Max(0, addressState.Failures - 1);
                addressState.LastSeenUtc = now;
            }
        }
    }

    private AttemptState? Find(string key) => _states.TryGetValue(key, out var state) ? state : null;

    private void Record(string key, int limit, DateTimeOffset now)
    {
        if (!_states.TryGetValue(key, out var state))
        {
            state = new AttemptState { WindowStartedUtc = now };
            _states[key] = state;
        }
        if (now - state.WindowStartedUtc > FailureWindow)
        {
            state.Failures = 0;
            state.WindowStartedUtc = now;
        }
        state.Failures++;
        state.LastSeenUtc = now;
        if (state.Failures < limit) return;

        state.LockLevel = Math.Min(state.LockLevel + 1, 6);
        var multiplier = 1 << (state.LockLevel - 1);
        var duration = TimeSpan.FromTicks(Math.Min(BaseLockDuration.Ticks * multiplier, MaximumLockDuration.Ticks));
        state.LockedUntilUtc = now + duration;
        state.Failures = 0;
        state.WindowStartedUtc = now;
    }

    private void Prune(DateTimeOffset now)
    {
        foreach (var key in _states.Where(pair => now - pair.Value.LastSeenUtc > StateRetention && pair.Value.LockedUntilUtc <= now)
                     .Select(pair => pair.Key).ToArray())
            _states.Remove(key);
    }

    private static string AddressKey(string value) => "ip:" + (string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim());
    private static string EmailKey(string value) => "email:" + (value ?? string.Empty).Trim().ToLowerInvariant();

    private sealed class AttemptState
    {
        public int Failures { get; set; }
        public int LockLevel { get; set; }
        public DateTimeOffset WindowStartedUtc { get; set; }
        public DateTimeOffset LockedUntilUtc { get; set; }
        public DateTimeOffset LastSeenUtc { get; set; }
    }
}
