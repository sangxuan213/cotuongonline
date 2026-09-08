using System.Text.Json;
using XiangqiOnline.Persistence.Services;
using XiangqiOnline.Server.Accounts;
using XiangqiOnline.Server.Lobby;
using XiangqiOnline.Shared.Protocol;

namespace XiangqiOnline.Server.Networking;

public sealed class AccountMessageHandler
{
    private readonly AccountService _accounts;
    private readonly IPasswordResetEmailSender _email;
    private readonly PlayerSessionDirectory _directory;
    private readonly LoginAttemptLimiter _loginAttempts;

    public AccountMessageHandler(AccountService accounts, IPasswordResetEmailSender email, PlayerSessionDirectory directory,
        LoginAttemptLimiter? loginAttempts = null)
    { _accounts = accounts; _email = email; _directory = directory; _loginAttempts = loginAttempts ?? new LoginAttemptLimiter(); }

    public async Task RegisterAsync(RequestEnvelope<JsonElement> request, ClientConnectionHandler connection, CancellationToken ct)
    {
        var result = _accounts.Register(Read(request.Payload, "email"), Read(request.Payload, "displayName"), Read(request.Payload, "password"), DateTimeOffset.UtcNow);
        if (!result.Success) { await SendAsync(connection, "ACCOUNT_REGISTER_RESULT", request.RequestId, new { status = "REJECTED", errorCode = result.Code, message = result.Message }, ct); return; }
        await SendAsync(connection, "ACCOUNT_REGISTER_RESULT", request.RequestId, new { status = "ACCEPTED", message = result.Message }, ct);
        await LoginMessageHandler.SendLoginResultAsync(result.Account!.DisplayName, request.RequestId, connection, _directory, ct,
            "ACCOUNT_" + result.Account.AccountId);
    }

    public async Task LoginAsync(RequestEnvelope<JsonElement> request, ClientConnectionHandler connection, CancellationToken ct)
    {
        var email = Read(request.Payload, "email").Trim();
        var now = DateTimeOffset.UtcNow;
        if (!_loginAttempts.CanAttempt(connection.RemoteAddress, email, now, out var retryAfter))
        {
            await SendAsync(connection, "LOGIN_RESULT", request.RequestId, new
            {
                status = "REJECTED",
                errorCode = "LOGIN_RATE_LIMITED",
                message = "Đăng nhập sai quá nhiều lần. Vui lòng thử lại sau.",
                retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds))
            }, ct);
            return;
        }
        var result = _accounts.Authenticate(email, Read(request.Payload, "password"));
        if (!result.Success)
        {
            _loginAttempts.RecordFailure(connection.RemoteAddress, email, now);
            await SendAsync(connection, "LOGIN_RESULT", request.RequestId, new { status = "REJECTED", errorCode = result.Code, message = result.Message }, ct);
            return;
        }
        _loginAttempts.RecordSuccess(connection.RemoteAddress, email, now);
        await LoginMessageHandler.SendLoginResultAsync(result.Account!.DisplayName, request.RequestId, connection, _directory, ct,
            "ACCOUNT_" + result.Account.AccountId);
    }

    public async Task RequestResetAsync(RequestEnvelope<JsonElement> request, ClientConnectionHandler connection, CancellationToken ct)
    {
        var email = Read(request.Payload, "email").Trim();
        var issue = _accounts.IssuePasswordReset(email, DateTimeOffset.UtcNow);
        var sent = false;
        if (issue.ShouldSend)
        {
            sent = await _email.SendAsync(email, issue.DisplayName!, issue.Code!, ct).ConfigureAwait(false);
            ServerConsoleLog.Info("TÀI KHOẢN", sent ? "Đã gửi một email đặt lại mật khẩu." : "Yêu cầu đặt lại đã nhận nhưng SMTP chưa gửi được email.");
            if (!sent)
                ServerConsoleLog.Warning("KHÔI PHỤC", $"Mã dự phòng cho {MaskEmail(email)}: {issue.Code} (hết hạn sau 10 phút).");
        }
        var message = !_email.IsConfigured
            ? "Email máy chủ chưa được cấu hình. Nếu tài khoản tồn tại, mã dự phòng đã được ghi trong tab Nhật ký hoạt động của Server."
            : issue.Message + " Nếu chưa nhận được email, hãy kiểm tra Spam hoặc liên hệ quản trị viên.";
        await SendAsync(connection, "PASSWORD_RESET_SENT", request.RequestId, new { message, deliveryStatus = sent ? "SENT" : _email.IsConfigured ? "PENDING_OR_FAILED" : "SMTP_NOT_CONFIGURED" }, ct);
    }

    public async Task ConfirmResetAsync(RequestEnvelope<JsonElement> request, ClientConnectionHandler connection, CancellationToken ct)
    {
        var result = _accounts.ResetPassword(Read(request.Payload, "email"), Read(request.Payload, "code"), Read(request.Payload, "newPassword"), DateTimeOffset.UtcNow);
        await SendAsync(connection, "PASSWORD_RESET_RESULT", request.RequestId,
            new { status = result.Success ? "ACCEPTED" : "REJECTED", errorCode = result.Success ? null : result.Code, message = result.Message }, ct);
    }

    private static string Read(JsonElement? payload, string name) => payload is { } p && p.TryGetProperty(name, out var node) && node.ValueKind == JsonValueKind.String ? node.GetString() ?? string.Empty : string.Empty;
    private static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');
        if (at <= 1) return "***";
        return email[..1] + new string('*', Math.Min(6, at - 1)) + email[at..];
    }
    private static Task SendAsync(ClientConnectionHandler connection, string type, string requestId, object payload, CancellationToken ct) => connection.SendAsync(new ServerEventEnvelope<object>
    { Type = type, EventId = Guid.NewGuid().ToString("N"), CausationRequestId = requestId, ServerTimeUtc = DateTimeOffset.UtcNow, Payload = payload }, ct);
}
