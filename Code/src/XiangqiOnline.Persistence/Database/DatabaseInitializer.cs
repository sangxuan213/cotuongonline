using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using XiangqiOnline.Persistence.Configuration;

namespace XiangqiOnline.Persistence.Database;

/// <summary>
/// Khởi tạo database mới (fresh DB) và áp dụng schema migration idempotent.
/// Migration được theo dõi qua bảng <c>schema_versions</c>.
/// </summary>
public sealed class DatabaseInitializer
{
    private readonly DatabaseOptions _options;
    private readonly ILogger<DatabaseInitializer> _logger;

    public DatabaseInitializer(DatabaseOptions options, ILogger<DatabaseInitializer> logger)
    {
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Tạo database (nếu chưa có), đảm bảo thư mục cha tồn tại, và áp dụng schema.
    /// Idempotent: chạy nhiều lần an toàn.
    /// </summary>
    public void Initialize()
    {
        EnsureParentDirectory();
        using var connection = new SqliteConnection(_options.BuildConnectionString());
        connection.Open();

        var schema = ReadEmbeddedSchema();
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = schema;
            cmd.ExecuteNonQuery();
        }

        _logger.LogInformation("Database initialized. Schema version {Version} applied.", CurrentSchemaVersion(connection));
    }

    /// <summary>
    /// Chạy migration trên một connection đang mở (dùng trong tests với in-memory / temp DB).
    /// </summary>
    public static void ApplySchema(SqliteConnection connection)
    {
        var schema = ReadEmbeddedSchema();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = schema;
        cmd.ExecuteNonQuery();
    }

    private void EnsureParentDirectory()
    {
        if (string.IsNullOrWhiteSpace(_options.DatabasePath))
        {
            return;
        }

        var fullPath = Path.GetFullPath(_options.DatabasePath);
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    private static string ReadEmbeddedSchema()
    {
        // Schema.sql được copy to output (CopyToOutputDirectory=PreserveNewest).
        var searchPaths = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Database", "Schema.sql"),
            Path.Combine(Path.GetFullPath("."), "Database", "Schema.sql"),
            Path.Combine(AppContext.BaseDirectory, "Schema.sql")
        };

        foreach (var path in searchPaths)
        {
            if (File.Exists(path))
            {
                return File.ReadAllText(path);
            }
        }

        throw new FileNotFoundException("Không tìm thấy Schema.sql");
    }

    private static int CurrentSchemaVersion(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_versions;";
        var result = cmd.ExecuteScalar();
        return result is null ? 0 : Convert.ToInt32(result);
    }
}
