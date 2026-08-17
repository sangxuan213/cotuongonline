using Microsoft.Extensions.Logging;
using XiangqiOnline.Persistence.Logging;

namespace XiangqiOnline.IntegrationTests.Logging;

/// <summary>
/// TV6 Phase 1 logging tests (P1-TV6-D4):
/// - SecretRedactor redacts token/password/secret
/// - CorrelationContext creates scoped correlation id
/// - LoggingSetup builds a logger factory (structured Serilog) without crashing
/// </summary>
public sealed class Tv6LoggingTests
{
    [Fact]
    public void SecretRedactor_redacts_password_token_secret()
    {
        var input = "password=hunter2 token=abc123 secret=xyz conn=Data Source=db;Password=p@ss";
        var redacted = SecretRedactor.Redact(input);

        Assert.DoesNotContain("hunter2", redacted);
        Assert.DoesNotContain("abc123", redacted);
        Assert.DoesNotContain("xyz", redacted);
        Assert.DoesNotContain("p@ss", redacted);
        Assert.Contains("[REDACTED]", redacted);
    }

    [Fact]
    public void SecretRedactor_does_not_throw_for_null_or_empty()
    {
        Assert.Equal(string.Empty, SecretRedactor.Redact(null));
        Assert.Equal(string.Empty, SecretRedactor.Redact(string.Empty));
    }

    [Fact]
    public void CorrelationContext_creates_scope_with_id()
    {
        var loggerFactory = LoggingSetup.CreateLoggerFactory(System.IO.Path.GetTempPath());
        var logger = loggerFactory.CreateLogger("test");
        var id = CorrelationContext.NewId();
        Assert.False(string.IsNullOrWhiteSpace(id));

        using var scope = CorrelationContext.BeginScope(logger, id);
        // scope should be non-null
        Assert.NotNull(scope);
    }

    [Fact]
    public void LoggingSetup_builds_logger_factory_without_crashing()
    {
        var factory = LoggingSetup.CreateLoggerFactory(System.IO.Path.GetTempPath());
        var logger = factory.CreateLogger("test");
        // Logging must never throw / crash business flow
        logger.LogInformation("Test structured log {Kvp}", new { Correlation = "abc" });
        Assert.NotNull(logger);
    }

    [Fact]
    public void Logging_writes_to_file_without_crashing()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"tv6-log-{Guid.NewGuid():N}");
        var factory = LoggingSetup.CreateLoggerFactory(dir);
        var logger = factory.CreateLogger("test");
        logger.LogInformation("Hello {World}", "TV6");

        // Give the file sink a moment to flush
        System.Threading.Thread.Sleep(200);

        // File should exist (created by rolling file sink)
        var files = System.IO.Directory.GetFiles(dir, "*.log");
        Assert.NotEmpty(files);
    }
}
