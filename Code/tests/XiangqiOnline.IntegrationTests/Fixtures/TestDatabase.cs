using Microsoft.Extensions.Logging;
using XiangqiOnline.Persistence.Configuration;
using XiangqiOnline.Persistence.Database;
using XiangqiOnline.Persistence.Logging;
using XiangqiOnline.Persistence.Services;

namespace XiangqiOnline.IntegrationTests.Fixtures;

/// <summary>
/// Tao database tam (temp file) cho integration tests. Moi instance cach ly DB rieng.
/// </summary>
public sealed class TestDatabase : IDisposable
{
    private string? _tempFile;

    public DatabaseOptions Options { get; }
    public ILoggerFactory LoggerFactory { get; }
    public GamePersistenceService Service { get; }

    private TestDatabase(DatabaseOptions options, ILoggerFactory loggerFactory)
    {
        Options = options;
        LoggerFactory = loggerFactory;
        Service = new GamePersistenceService(options, loggerFactory);
        Service.InitializeDatabase();
    }

    /// <summary>
    /// Tao TestDatabase dua tren file da co san (dung de test rollback / persistence failure
    /// voi DB hop le). Neu null path thi tao temp file moi.
    /// </summary>
    public static TestDatabase Create(string? existingDbPath = null)
    {
        var loggerFactory = LoggingSetup.CreateLoggerFactory(System.IO.Path.GetTempPath());
        if (existingDbPath != null)
        {
            return new TestDatabase(new DatabaseOptions { DatabasePath = existingDbPath }, loggerFactory);
        }

        var tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"tv6-test-{Guid.NewGuid():N}.db");
        var options = new DatabaseOptions { DatabasePath = tempFile };
        var db = new TestDatabase(options, loggerFactory);
        db._tempFile = tempFile;
        return db;
    }

    public void Dispose()
    {
        if (_tempFile != null && System.IO.File.Exists(_tempFile))
        {
            try { System.IO.File.Delete(_tempFile); } catch { /* best effort */ }
        }
        LoggerFactory?.Dispose();
    }
}
