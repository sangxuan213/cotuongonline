using Microsoft.Data.Sqlite;
using XiangqiOnline.Persistence.Configuration;

namespace XiangqiOnline.Persistence.Database;

/// <summary>
/// Default implementation tạo connection từ <see cref="DatabaseOptions"/>.
/// </summary>
public sealed class DbConnectionFactory : IDbConnectionFactory
{
    private readonly DatabaseOptions _options;
    private readonly bool _keepAlive;

    public DbConnectionFactory(DatabaseOptions options, bool keepAlive = false)
    {
        _options = options;
        _keepAlive = keepAlive;
    }

    public SqliteConnection CreateConnection()
    {
        var conn = new SqliteConnection(_options.BuildConnectionString());
        if (_keepAlive)
        {
            conn.Open();
        }
        return conn;
    }
}
