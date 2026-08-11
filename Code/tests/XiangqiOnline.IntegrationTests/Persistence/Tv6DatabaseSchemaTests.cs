using Microsoft.Data.Sqlite;
using XiangqiOnline.Persistence.Database;

namespace XiangqiOnline.IntegrationTests.Persistence;

/// <summary>
/// Schema conformance tests asserted against locked DDL UDM18_Database_Schema_v1.1.sql (P1-TV6-D1).
/// SHA-256: a0ae63e656f59ebfc876eed84fed8d3ee967b7f523983fa5e35d243915592ad1.
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
    public void Schema_sha256_matches_locked_ddl_v1_1()
    {
        var actualHash = DatabaseInitializer.GetCurrentSchemaSha256();
        Assert.Equal(DatabaseInitializer.LockedSchemaSha256, actualHash);
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
    public void Schema_version_1_1_is_seeded()
    {
        using var conn = OpenFreshDb();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM schema_versions WHERE version = '1.1';";
        var count = Convert.ToInt32(cmd.ExecuteScalar());
        Assert.Equal(1, count);
    }

    [Fact]
    public void Foreign_key_is_enforced_on_moves_to_matches()
    {
        using var conn = OpenFreshDb();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO moves (
                move_id, client_move_id, match_id, move_index, revision, side,
                piece_id, piece_type, from_x, from_y, to_x, to_y, move_class,
                is_capture, is_check, is_checkmate, red_remaining_ms, black_remaining_ms,
                board_hash_before, board_hash_after, created_at_utc
            ) VALUES (
                'm1', 'c1', 'no-such-match', 1, 1, 'RED',
                'P1', 'PAWN', 0, 0, 0, 1, 'IDLE',
                0, 0, 0, 600000, 600000,
                'h1', 'h2', '2026-01-01T00:00:00.000Z'
            );";
        var ex = Assert.Throws<SqliteException>(() => cmd.ExecuteNonQuery());
        Assert.True(ex.SqliteErrorCode == 19, "Expected FK constraint violation (SQLITE_CONSTRAINT).");
    }

    [Fact]
    public void Unique_constraint_on_match_client_move()
    {
        using var conn = OpenFreshDb();

        // Seed players and match
        using (var seedPlayers = conn.CreateCommand())
        {
            seedPlayers.CommandText = @"
                INSERT INTO players (player_id, display_name, created_at_utc)
                VALUES ('p1', 'Player 1', '2026-01-01T00:00:00.000Z'),
                       ('p2', 'Player 2', '2026-01-01T00:00:00.000Z');";
            seedPlayers.ExecuteNonQuery();
        }

        using (var seedMatch = conn.CreateCommand())
        {
            seedMatch.CommandText = @"
                INSERT INTO matches (
                    match_id, room_id, red_player_id, black_player_id, rule_profile_id,
                    rule_profile_version, time_profile, config_json, status, started_at_utc, total_moves
                ) VALUES (
                    'm1', 'room-1', 'p1', 'p2', 'UDM18_WXF_PRO_2018',
                    '1.1', 'STANDARD', '{}', 'PLAYING', '2026-01-01T00:00:00.000Z', 0
                );";
            seedMatch.ExecuteNonQuery();
        }

        // Insert first move
        using (var insert = conn.CreateCommand())
        {
            insert.CommandText = @"
                INSERT INTO moves (
                    move_id, client_move_id, match_id, move_index, revision, side,
                    piece_id, piece_type, from_x, from_y, to_x, to_y, move_class,
                    is_capture, is_check, is_checkmate, red_remaining_ms, black_remaining_ms,
                    board_hash_before, board_hash_after, created_at_utc
                ) VALUES (
                    'mv1', 'c1', 'm1', 1, 1, 'RED',
                    'P1', 'PAWN', 0, 0, 0, 1, 'IDLE',
                    0, 0, 0, 600000, 600000,
                    'h1', 'h2', '2026-01-01T00:00:00.000Z'
                );";
            insert.ExecuteNonQuery();
        }

        // Duplicate client_move_id for same match -> must throw unique constraint violation
        using var dup = conn.CreateCommand();
        dup.CommandText = @"
            INSERT INTO moves (
                move_id, client_move_id, match_id, move_index, revision, side,
                piece_id, piece_type, from_x, from_y, to_x, to_y, move_class,
                is_capture, is_check, is_checkmate, red_remaining_ms, black_remaining_ms,
                board_hash_before, board_hash_after, created_at_utc
            ) VALUES (
                'mv2', 'c1', 'm1', 2, 2, 'BLACK',
                'P2', 'PAWN', 2, 2, 2, 3, 'IDLE',
                0, 0, 0, 600000, 600000,
                'h2', 'h3', '2026-01-01T00:00:00.000Z'
            );";
        var ex = Assert.Throws<SqliteException>(() => dup.ExecuteNonQuery());
        Assert.True(ex.SqliteErrorCode == 19, "Expected unique constraint violation.");
    }

    [Fact]
    public void Initializer_is_idempotent_on_fresh_db()
    {
        using var conn = OpenFreshDb();
        DatabaseInitializer.ApplySchema(conn);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM schema_versions WHERE version = '1.1';";
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
