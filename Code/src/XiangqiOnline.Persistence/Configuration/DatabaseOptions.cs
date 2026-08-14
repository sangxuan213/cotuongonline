namespace XiangqiOnline.Persistence.Configuration;

/// <summary>
/// Cấu hình kết nối cho Persistence layer (TV6).
/// Connection string và DB path có thể được cấp qua environment variables
/// để CI / local dev có thể trỏ tới database riêng.
/// </summary>
public sealed record DatabaseOptions
{
    /// <summary>Đường dẫn tới file SQLite database.</summary>
    public string DatabasePath { get; init; } = "xiangqi.db";

    /// <summary>
    /// Connection string hoàn chỉnh. Nếu được cấp thì được dùng trực tiếp,
    /// ngược lại sẽ được build từ <see cref="DatabasePath"/>.
    /// </summary>
    public string? ConnectionString { get; init; }

    /// <summary>
    /// Chuỗi kết nối đầy đủ. Nếu ConnectionString không được cấp,
    /// build từ DatabasePath (hỗ trợ foreign keys bật sẵn).
    /// </summary>
    public string BuildConnectionString()
    {
        if (!string.IsNullOrWhiteSpace(ConnectionString))
        {
            return ConnectionString;
        }

        return string.IsNullOrWhiteSpace(DatabasePath)
            ? "Data Source=:memory:"
            : new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
            {
                DataSource = DatabasePath,
                Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadWriteCreate,
                ForeignKeys = true
            }.ToString();
    }

    /// <summary>
    /// Đọc cấu hình từ environment variables:
    ///   SERVER_DB_PATH  -> DatabasePath
    ///   SERVER_DB_CONNECTION_STRING -> ConnectionString
    /// Mặc định DatabasePath = "Extra/database/xiangqi.db" (tương đối theo thư mục làm việc).
    /// </summary>
    public static DatabaseOptions FromEnvironment()
    {
        var dbPath = Environment.GetEnvironmentVariable("SERVER_DB_PATH");
        var connStr = Environment.GetEnvironmentVariable("SERVER_DB_CONNECTION_STRING");

        return new DatabaseOptions
        {
            DatabasePath = string.IsNullOrWhiteSpace(dbPath) ? "Extra/database/xiangqi.db" : dbPath,
            ConnectionString = string.IsNullOrWhiteSpace(connStr) ? null : connStr
        };
    }
}
