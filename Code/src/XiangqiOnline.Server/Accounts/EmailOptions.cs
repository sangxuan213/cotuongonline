using Microsoft.Extensions.Configuration;

namespace XiangqiOnline.Server.Accounts;

public sealed class EmailOptions
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = "Cờ Tướng Online - Nhóm 6";
    public bool EnableSsl { get; set; } = true;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(FromAddress);

    public static EmailOptions FromConfiguration(Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        var value = configuration.GetSection("Email").Get<EmailOptions>() ?? new EmailOptions();
        value.Host = Environment.GetEnvironmentVariable("XIANGQI_SMTP_HOST") ?? value.Host;
        value.Username = Environment.GetEnvironmentVariable("XIANGQI_SMTP_USER") ?? value.Username;
        value.Password = Environment.GetEnvironmentVariable("XIANGQI_SMTP_PASSWORD") ?? value.Password;
        value.FromAddress = Environment.GetEnvironmentVariable("XIANGQI_SMTP_FROM") ?? value.FromAddress;
        if (int.TryParse(Environment.GetEnvironmentVariable("XIANGQI_SMTP_PORT"), out var port)) value.Port = port;
        return value;
    }
}
