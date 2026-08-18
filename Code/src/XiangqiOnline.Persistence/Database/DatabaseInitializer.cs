using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using XiangqiOnline.Persistence.Configuration;

namespace XiangqiOnline.Persistence.Database;

/// <summary>
/// Khởi tạo database mới (fresh DB) và áp dụng schema migration idempotent
/// dựa trên UDM18_Database_Schema_v1.1.sql khóa cứng.
/// Migration được theo dõi qua bảng <c>schema_versions</c>.
/// </summary>
public sealed class DatabaseInitializer
{
    public const string LockedSchemaSha256 = "a0ae63e656f59ebfc876eed84fed8d3ee967b7f523983fa5e35d243915592ad1";

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
            cmd.CommandText = ReadAccountSchema();
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
        cmd.CommandText = ReadAccountSchema();
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Tính và trả về SHA256 checksum của file Schema.sql hiện tại.
    /// </summary>
    public static string GetCurrentSchemaSha256()
    {
        var path = GetSchemaFilePath();
        var text = File.ReadAllText(path).Replace("\r\n", "\n");
        var bytes = Encoding.UTF8.GetBytes(text);
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(bytes);
        return Convert.ToHexStringLower(hashBytes);
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

    private static string GetSchemaFilePath()
    {
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
                return path;
            }
        }

        throw new FileNotFoundException("Không tìm thấy Schema.sql");
    }

    private static string ReadEmbeddedSchema()
    {
        return File.ReadAllText(GetSchemaFilePath());
    }

    private static string ReadAccountSchema()
    {
        var path = Path.Combine(Path.GetDirectoryName(GetSchemaFilePath())!, "AccountSchema.sql");
        if (!File.Exists(path)) throw new FileNotFoundException("Không tìm thấy AccountSchema.sql", path);
        return File.ReadAllText(path);
    }

    private static string CurrentSchemaVersion(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(MAX(version), '0') FROM schema_versions;";
        var result = cmd.ExecuteScalar();
        return result?.ToString() ?? "0";
    }
}
