using Microsoft.Data.Sqlite;

namespace XiangqiOnline.Persistence.Database;

/// <summary>
/// Factory tạo SqliteConnection từ cấu hình. Cho phép tests dùng in-memory / temp DB.
/// </summary>
public interface IDbConnectionFactory
{
    SqliteConnection CreateConnection();
}
