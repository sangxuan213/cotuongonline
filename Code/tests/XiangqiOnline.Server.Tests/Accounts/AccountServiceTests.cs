using Microsoft.Extensions.Logging.Abstractions;
using XiangqiOnline.Persistence.Configuration;
using XiangqiOnline.Persistence.Services;

namespace XiangqiOnline.Server.Tests.Accounts;

public sealed class AccountServiceTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "xiangqi-accounts-" + Guid.NewGuid().ToString("N") + ".db");
    private readonly AccountService _accounts;

    public AccountServiceTests()
    {
        var options = new DatabaseOptions { DatabasePath = _path };
        new GamePersistenceService(options, NullLoggerFactory.Instance).InitializeDatabase();
        _accounts = new AccountService(options, "unit-test-reset-pepper-32-characters");
    }

    [Fact]
    public void Register_Authenticate_AndRejectWrongPassword()
    {
        var registered = _accounts.Register("player@example.com", "Kỳ Thủ", "Abcd1234", DateTimeOffset.UtcNow);
        Assert.True(registered.Success);
        Assert.True(_accounts.Authenticate("PLAYER@example.com", "Abcd1234").Success);
        Assert.False(_accounts.Authenticate("player@example.com", "wrong").Success);
        Assert.Equal("ACCOUNT_EXISTS", _accounts.Register("player@example.com", "Tên khác", "Abcd1234", DateTimeOffset.UtcNow).Code);
    }

    [Fact]
    public void Register_AcceptsSimplePassword_WhenLengthIsAtLeastEight()
    {
        var result = _accounts.Register("simple@example.com", "SimpleUser", "matkhau123", DateTimeOffset.UtcNow);
        Assert.True(result.Success);
        Assert.True(_accounts.Authenticate("simple@example.com", "matkhau123").Success);
        Assert.False(_accounts.Register("short@example.com", "ShortUser", "1234567", DateTimeOffset.UtcNow).Success);
    }

    [Fact]
    public void ResetCode_IsSingleUse_AndChangesPassword()
    {
        var now = DateTimeOffset.UtcNow;
        _accounts.Register("reset@example.com", "ResetUser", "Abcd1234", now);
        var issue = _accounts.IssuePasswordReset("reset@example.com", now.AddMinutes(1));
        Assert.True(issue.ShouldSend);
        Assert.Matches("^[0-9]{6}$", issue.Code!);
        Assert.False(_accounts.ResetPassword("reset@example.com", "000000", "Newpass9", now.AddMinutes(2)).Success);
        Assert.True(_accounts.ResetPassword("reset@example.com", issue.Code!, "Newpass9", now.AddMinutes(2)).Success);
        Assert.False(_accounts.ResetPassword("reset@example.com", issue.Code!, "Otherpass8", now.AddMinutes(3)).Success);
        Assert.False(_accounts.Authenticate("reset@example.com", "Abcd1234").Success);
        Assert.True(_accounts.Authenticate("reset@example.com", "Newpass9").Success);
    }

    [Fact]
    public void UnknownEmail_AndThrottle_ReturnSameGenericResponse()
    {
        var now = DateTimeOffset.UtcNow;
        _accounts.Register("known@example.com", "Known", "Abcd1234", now);
        var first = _accounts.IssuePasswordReset("known@example.com", now.AddMinutes(1));
        var throttled = _accounts.IssuePasswordReset("known@example.com", now.AddMinutes(1).AddSeconds(10));
        var unknown = _accounts.IssuePasswordReset("missing@example.com", now);
        Assert.True(first.ShouldSend);
        Assert.False(throttled.ShouldSend);
        Assert.False(unknown.ShouldSend);
        Assert.Equal(unknown.Message, throttled.Message);
    }

    [Fact]
    public void ExpiredResetCode_IsRejected()
    {
        var now = DateTimeOffset.UtcNow;
        _accounts.Register("expired@example.com", "Expired", "Abcd1234", now);
        var issue = _accounts.IssuePasswordReset("expired@example.com", now.AddMinutes(1));
        Assert.False(_accounts.ResetPassword("expired@example.com", issue.Code!, "Newpass9", now.AddMinutes(12)).Success);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_path)) File.Delete(_path);
    }
}
