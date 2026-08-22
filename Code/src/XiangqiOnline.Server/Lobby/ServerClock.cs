using XiangqiOnline.Shared.Enums;

namespace XiangqiOnline.Server.Lobby;

public sealed record TimeProfileSpec(string Id, TimeSpan Initial, TimeSpan Increment)
{
    public static TimeProfileSpec Parse(string? profile)
    {
        var normalized = (profile ?? string.Empty).Trim().ToUpperInvariant();
        return normalized switch
        {
            "60+30" or "STANDARD" or "STANDARD_PRO" or "COURSE_DEMO" => new("60+30", TimeSpan.FromMinutes(60), TimeSpan.FromSeconds(30)),
            "10+5" or "RAPID" => new("10+5", TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(5)),
            "10+0" or "BOT_STANDARD" => new("10+0", TimeSpan.FromMinutes(10), TimeSpan.Zero),
            "5+3" or "BLITZ" => new("5+3", TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(3)),
            "3+2" or "BULLET" => new("3+2", TimeSpan.FromMinutes(3), TimeSpan.FromSeconds(2)),
            _ => throw new ArgumentException($"Unsupported time profile '{profile}'.", nameof(profile))
        };
    }
}

public sealed record ClockSnapshot(
    long RedRemainingMs,
    long BlackRemainingMs,
    SideColor ActiveSide,
    long IncrementMs,
    DateTimeOffset ServerAnchorUtc,
    bool IsExpired);

public sealed record ClockMoveReservation(
    long Version,
    long CommitTimestamp,
    TimeSpan RedRemaining,
    TimeSpan BlackRemaining,
    SideColor NextActiveSide,
    ClockSnapshot Snapshot);

public sealed class ServerClock
{
    private readonly TimeProvider _timeProvider;
    private readonly object _gate = new();
    private long _turnStartedTimestamp;
    private long _version;
    private TimeSpan _redRemaining;
    private TimeSpan _blackRemaining;

    public ServerClock(TimeProfileSpec profile, SideColor activeSide = SideColor.Red, TimeProvider? timeProvider = null)
    {
        Profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _redRemaining = profile.Initial;
        _blackRemaining = profile.Initial;
        ActiveSide = activeSide;
        _turnStartedTimestamp = _timeProvider.GetTimestamp();
    }

    public TimeProfileSpec Profile { get; }
    public SideColor ActiveSide { get; private set; }
    public bool IsStopped { get; private set; }

    public ClockSnapshot Snapshot()
    {
        lock (_gate)
        {
            var (red, black) = CurrentRemainingUnsafe(_timeProvider.GetTimestamp());
            return CreateSnapshot(red, black, ActiveSide, _timeProvider.GetUtcNow());
        }
    }

    public bool TryPrepareMove(SideColor side, out ClockMoveReservation reservation, out ClockSnapshot snapshot)
    {
        lock (_gate)
        {
            var nowTimestamp = _timeProvider.GetTimestamp();
            var nowUtc = _timeProvider.GetUtcNow();
            var (red, black) = CurrentRemainingUnsafe(nowTimestamp);
            if (IsStopped || side != ActiveSide || (side == SideColor.Red ? red : black) <= TimeSpan.Zero)
            {
                snapshot = CreateSnapshot(red, black, ActiveSide, nowUtc);
                reservation = null!;
                return false;
            }

            if (side == SideColor.Red) red += Profile.Increment;
            else black += Profile.Increment;
            var nextSide = side == SideColor.Red ? SideColor.Black : SideColor.Red;
            snapshot = CreateSnapshot(red, black, nextSide, nowUtc);
            reservation = new ClockMoveReservation(_version, nowTimestamp, red, black, nextSide, snapshot);
            return true;
        }
    }

    public ClockSnapshot CommitPreparedMove(ClockMoveReservation reservation)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        lock (_gate)
        {
            if (IsStopped || reservation.Version != _version || reservation.NextActiveSide == ActiveSide)
                throw new InvalidOperationException("Clock changed after the move was reserved.");
            _redRemaining = reservation.RedRemaining;
            _blackRemaining = reservation.BlackRemaining;
            ActiveSide = reservation.NextActiveSide;
            _turnStartedTimestamp = reservation.CommitTimestamp;
            _version++;
            return reservation.Snapshot;
        }
    }

    public bool TryCommitMove(SideColor side, out ClockSnapshot snapshot)
    {
        if (!TryPrepareMove(side, out var reservation, out snapshot)) return false;
        snapshot = CommitPreparedMove(reservation);
        return true;
    }

    public SideColor? GetExpiredSide()
    {
        lock (_gate)
        {
            var now = _timeProvider.GetTimestamp();
            var (red, black) = CurrentRemainingUnsafe(now);
            if (red > TimeSpan.Zero && black > TimeSpan.Zero) return null;
            if (!IsStopped)
            {
                ApplyElapsedUnsafe(now);
                IsStopped = true;
                _version++;
            }
            return red <= TimeSpan.Zero ? SideColor.Red : SideColor.Black;
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (IsStopped) return;
            ApplyElapsedUnsafe(_timeProvider.GetTimestamp());
            IsStopped = true;
            _version++;
        }
    }

    private ClockSnapshot CreateSnapshot(TimeSpan red, TimeSpan black, SideColor activeSide, DateTimeOffset anchor) => new(
        Math.Max(0, (long)red.TotalMilliseconds),
        Math.Max(0, (long)black.TotalMilliseconds),
        activeSide,
        (long)Profile.Increment.TotalMilliseconds,
        anchor,
        red <= TimeSpan.Zero || black <= TimeSpan.Zero);

    private (TimeSpan Red, TimeSpan Black) CurrentRemainingUnsafe(long nowTimestamp)
    {
        if (IsStopped) return (_redRemaining, _blackRemaining);
        var elapsed = _timeProvider.GetElapsedTime(_turnStartedTimestamp, nowTimestamp);
        return ActiveSide == SideColor.Red
            ? (_redRemaining - elapsed, _blackRemaining)
            : (_redRemaining, _blackRemaining - elapsed);
    }

    private void ApplyElapsedUnsafe(long nowTimestamp)
    {
        if (IsStopped) return;
        var elapsed = _timeProvider.GetElapsedTime(_turnStartedTimestamp, nowTimestamp);
        if (ActiveSide == SideColor.Red) _redRemaining -= elapsed;
        else _blackRemaining -= elapsed;
        _turnStartedTimestamp = nowTimestamp;
    }
}
