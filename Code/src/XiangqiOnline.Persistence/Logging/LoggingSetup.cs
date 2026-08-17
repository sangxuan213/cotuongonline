using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Formatting.Compact;

namespace XiangqiOnline.Persistence.Logging;

/// <summary>
/// Cau hinh logging cho persistence layer (TV6 D4):
/// - Serilog + Microsoft.Extensions.Logging
/// - Structured logging (JSON compact)
/// - Correlation id scoped
/// - Redaction token/secret
/// - Logging khong lam crash nghiep vu (fallback NullLogger)
/// </summary>
public static class LoggingSetup
{
    /// <summary>
    /// Ten thu muc log mac dinh (theo repo convention Extra/logs).
    /// </summary>
    public const string DefaultLogDirectory = "Extra/logs";

    /// <summary>
    /// Build logger factory voi Serilog. Khong nem exception khi khong cau hinh duoc
    /// (fallback NullLogger).
    /// </summary>
    public static ILoggerFactory CreateLoggerFactory(string logDirectory = DefaultLogDirectory)
    {
        try
        {
            var logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .Enrich.FromLogContext()
                .WriteTo.Console(new CompactJsonFormatter())
                .WriteTo.File(
                    new CompactJsonFormatter(),
                    System.IO.Path.Combine(logDirectory, "tv6-persistence-.log"),
                    rollingInterval: Serilog.RollingInterval.Day)
                .CreateLogger();

            return new ServiceCollection()
                .AddLogging(builder =>
                {
                    builder.ClearProviders();
                    builder.AddSerilog(logger, dispose: true);
                })
                .BuildServiceProvider()
                .GetRequiredService<ILoggerFactory>();
        }
        catch
        {
            return new ServiceCollection()
                .AddLogging(builder => builder.AddProvider(Microsoft.Extensions.Logging.Abstractions.NullLoggerProvider.Instance))
                .BuildServiceProvider()
                .GetRequiredService<ILoggerFactory>();
        }
    }
}
