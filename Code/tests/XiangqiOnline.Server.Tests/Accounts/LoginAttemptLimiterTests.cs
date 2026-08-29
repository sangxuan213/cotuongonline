using XiangqiOnline.Server.Accounts;

namespace XiangqiOnline.Server.Tests.Accounts;

public sealed class LoginAttemptLimiterTests
{
    [Fact]
    public void Fifth_failure_locks_email_before_another_password_hash()
    {
        var limiter = new LoginAttemptLimiter();
        var now = DateTimeOffset.Parse("2026-08-16T00:00:00Z");
        for (var attempt = 0; attempt < 5; attempt++)
            limiter.RecordFailure("203.0.113.8", "User@Example.com", now.AddSeconds(attempt));

        Assert.False(limiter.CanAttempt("198.51.100.4", "user@example.com", now.AddSeconds(5), out var retry));
        Assert.InRange(retry.TotalSeconds, 28, 30);
    }

    [Fact]
    public void Reconnecting_does_not_bypass_email_lock()
    {
        var limiter = new LoginAttemptLimiter();
        var now = DateTimeOffset.Parse("2026-08-16T00:00:00Z");
        for (var attempt = 0; attempt < 5; attempt++)
            limiter.RecordFailure($"203.0.113.{attempt + 1}", "target@example.com", now);

        Assert.False(limiter.CanAttempt("192.0.2.99", "target@example.com", now, out _));
    }

    [Fact]
    public void Successful_login_clears_email_failures()
    {
        var limiter = new LoginAttemptLimiter();
        var now = DateTimeOffset.Parse("2026-08-16T00:00:00Z");
        for (var attempt = 0; attempt < 4; attempt++)
            limiter.RecordFailure("203.0.113.8", "user@example.com", now);

        limiter.RecordSuccess("203.0.113.8", "user@example.com", now);

        Assert.True(limiter.CanAttempt("203.0.113.9", "user@example.com", now, out _));
    }

    [Fact]
    public void Lock_expires_after_retry_window()
    {
        var limiter = new LoginAttemptLimiter();
        var now = DateTimeOffset.Parse("2026-08-16T00:00:00Z");
        for (var attempt = 0; attempt < 5; attempt++)
            limiter.RecordFailure("203.0.113.8", "user@example.com", now);

        Assert.True(limiter.CanAttempt("203.0.113.8", "user@example.com", now.AddSeconds(31), out _));
    }
}
