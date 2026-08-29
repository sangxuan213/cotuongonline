using System.Net;
using System.Net.Mail;

namespace XiangqiOnline.Server.Accounts;

public interface IPasswordResetEmailSender
{
    bool IsConfigured => true;
    Task<bool> SendAsync(string recipient, string displayName, string code, CancellationToken cancellationToken);
}

public sealed class SmtpPasswordResetEmailSender : IPasswordResetEmailSender
{
    private readonly EmailOptions _options;
    public SmtpPasswordResetEmailSender(EmailOptions options) => _options = options;
    public bool IsConfigured => _options.IsConfigured;

    public async Task<bool> SendAsync(string recipient, string displayName, string code, CancellationToken cancellationToken)
    {
        if (!_options.IsConfigured) return false;
        using var message = new MailMessage
        {
            From = new MailAddress(_options.FromAddress, _options.FromName),
            Subject = "Mã đặt lại mật khẩu Cờ Tướng Online",
            Body = $"Xin chào {displayName},\n\nMã xác nhận của bạn là: {code}\nMã có hiệu lực trong 10 phút và chỉ dùng một lần.\n\nNếu bạn không yêu cầu, hãy bỏ qua email này.",
            IsBodyHtml = false
        };
        message.To.Add(recipient);
        using var client = new SmtpClient(_options.Host, _options.Port)
        {
            EnableSsl = _options.EnableSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(_options.Username, _options.Password)
        };
        try
        {
            await client.SendMailAsync(message, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            ServerConsoleLog.Warning("EMAIL", $"Không gửi được email đặt lại mật khẩu: {ex.GetType().Name}");
            return false;
        }
    }
}
