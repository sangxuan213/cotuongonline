using Microsoft.Data.Sqlite;
using XiangqiOnline.Persistence.Database;
using XiangqiOnline.Persistence.Logging;

namespace XiangqiOnline.IntegrationTests.Persistence;

/// <summary>
/// TV6 Phase 1 schema/migration tests (P1-TV6-D1) on a fresh in-memory SQLite DB.
/// Verifies all 5 tables exist, foreign keys are enforced, and unique constraint holds.
/// </summary>
public sealed class Tv6DatabaseSchemaTests
{
    private static SqliteConnection OpenFreshDb()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        DatabaseInitializer.ApplySchema(conn);
        return conn;
    }

    [Fact]
    public void Fresh_db_has_all_required_tables()
    {
        using var conn = OpenFreshDb();
        var required = new[] { "schema_versions", "players", "matches", "moves", "position_history" };
        foreach (var table in required)
        {
            Assert.True(TableExists(conn, table), $"Expected table '{table}' to exist.");
        }
    }

    [Fact]
    public void Schema_version_1_is_seeded()
    {
        using var conn = OpenFreshDb();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM schema_versions WHERE version = 1;";
        var count = Convert.ToInt32(cmd.ExecuteScalar());
        Assert.Equal(1, count);
    }

    [Fact]
    public void Foreign_key_is_enforced_on_moves_to_matches()
    {
        using var conn = OpenFreshDb();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO moves (move_id, match_id, client_move_id, piece_id, from_x, from_y, to_x, to_y, move_number)
            VALUES ('m1', 'no-such-match', 'c1', 'P1', 0, 0, 0, 1, 1);";
        var ex = Assert.Throws<SqliteException>(() => cmd.ExecuteNonQuery());
        Assert.True(ex.SqliteErrorCode == 19, "Expected FK constraint violation (SQLITE_CONSTRAINT).");
    }

    [Fact]
    public void Unique_constraint_on_match_client_move()
    {
        using var conn = OpenFreshDb();
        // seed a match
        using (var seed = conn.CreateCommand())
        {
            seed.CommandText = "INSERT INTO matches (match_id) VALUES ('m1');";
            seed.ExecuteNonQuery();
        }
        // insert first move
        using (var insert = conn.CreateCommand())
        {
            insert.CommandText = @"
                INSERT INTO moves (move_id, match_id, client_move_id, piece_id, from_x, from_y, to_x, to_y, move_number)
                VALUES ('mv1', 'm1', 'c1', 'P1', 0, 0, 0, 1, 1);";
            insert.ExecuteNonQuery();
        }
        // duplicate client_move_id for same match -> must throw
        using var dup = conn.CreateCommand();
        dup.CommandText = @"
            INSERT INTO moves (move_id, match_id, client_move_id, piece_id, from_x, from_y, to_x, to_y, move_number)
            VALUES ('mv2', 'm1', 'c1', 'P2', 2, 2, 2, 3, 2);";
        var ex = Assert.Throws<SqliteException>(() => dup.ExecuteNonQuery());
        Assert.True(ex.SqliteErrorCode == 19, "Expected unique constraint violation.");
    }

    [Fact]
    public void Initializer_is_idempotent_on_fresh_db()
    {
        using var conn = OpenFreshDb();
        // Apply schema a second time - should not throw (CREATE TABLE IF NOT EXISTS)
        DatabaseInitializer.ApplySchema(conn);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM schema_versions WHERE version = 1;";
        Assert.Equal(1, Convert.ToInt32(cmd.ExecuteScalar()));
    }

    private static bool TableExists(SqliteConnection conn, string table)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@name;";
        cmd.Parameters.AddWithValue("@name", table);
        return Convert.ToInt32(cmd.ExecuteScalar()) == 1;
    }
}
