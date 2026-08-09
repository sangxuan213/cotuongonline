using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using XiangqiOnline.Persistence.Database;
using XiangqiOnline.Persistence.Models;
using XiangqiOnline.Shared.Models;

namespace XiangqiOnline.Persistence.Repositories;

/// <summary>
/// Repository cho bảng <c>moves</c>. Tất cả SQL đều dùng parameterized parameters.
/// Unique constraint (match_id, client_move_id) bảo vệ duplicate retry.
/// </summary>
public sealed class MoveRepository : IMoveRepository
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly SqliteConnection? _externalConnection;
    private readonly ILogger<MoveRepository> _logger;

    public MoveRepository(IDbConnectionFactory connectionFactory, ILogger<MoveRepository> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public MoveRepository(SqliteConnection connection, ILogger<MoveRepository> logger)
    {
        _externalConnection = connection;
        _logger = logger;
    }

    private SqliteConnection GetConnection()
    {
        return _externalConnection ?? _connectionFactory.CreateConnection();
    }

    public bool TryInsert(MoveRecord move)
    {
        var conn = GetConnection();
        var opened = EnsureOpen(conn);
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO moves
                    (move_id, match_id, client_move_id, piece_id, from_x, from_y, to_x, to_y,
                     captured_piece_id, board_hash_before, board_hash_after, move_number, result)
                VALUES
                    (@moveId, @matchId, @clientMoveId, @pieceId, @fromX, @fromY, @toX, @toY,
                     @captured, @hashBefore, @hashAfter, @moveNumber, @result);";

            cmd.Parameters.AddWithValue("@moveId", move.MoveId);
            cmd.Parameters.AddWithValue("@matchId", move.MatchId);
            cmd.Parameters.AddWithValue("@clientMoveId", move.ClientMoveId);
            cmd.Parameters.AddWithValue("@pieceId", move.PieceId);
            cmd.Parameters.AddWithValue("@fromX", move.From.X);
            cmd.Parameters.AddWithValue("@fromY", move.From.Y);
            cmd.Parameters.AddWithValue("@toX", move.To.X);
            cmd.Parameters.AddWithValue("@toY", move.To.Y);
            cmd.Parameters.AddWithValue("@captured", (object?)move.CapturedPieceId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@hashBefore", move.BoardHashBefore);
            cmd.Parameters.AddWithValue("@hashAfter", move.BoardHashAfter);
            cmd.Parameters.AddWithValue("@moveNumber", move.MoveNumber);
            cmd.Parameters.AddWithValue("@result", move.Result);

            cmd.ExecuteNonQuery();
            return true;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19 && ex.SqliteExtendedErrorCode == 2067) // SQLITE_CONSTRAINT_UNIQUE
        {
            // Duplicate (match_id, client_move_id) -> unique constraint violation.
            _logger.LogWarning("Duplicate clientMoveId detected. matchId={MatchId} clientMoveId={ClientMoveId}",
                move.MatchId, move.ClientMoveId);
            return false;
        }
        finally
        {
            if (opened) conn.Dispose();
        }
    }

    public MoveRecord? GetByClientMoveId(string matchId, string clientMoveId)
    {
        var conn = GetConnection();
        var opened = EnsureOpen(conn);
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT move_id, match_id, client_move_id, piece_id, from_x, from_y, to_x, to_y,
                       captured_piece_id, board_hash_before, board_hash_after, move_number, result, created_at_utc
                FROM moves
                WHERE match_id = @matchId AND client_move_id = @clientMoveId
                LIMIT 1;";
            cmd.Parameters.AddWithValue("@matchId", matchId);
            cmd.Parameters.AddWithValue("@clientMoveId", clientMoveId);

            using var reader = cmd.ExecuteReader();
            return reader.Read() ? MapMove(reader) : null;
        }
        finally
        {
            if (opened) conn.Dispose();
        }
    }

    public int CountByMatch(string matchId)
    {
        var conn = GetConnection();
        var opened = EnsureOpen(conn);
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM moves WHERE match_id = @matchId;";
            cmd.Parameters.AddWithValue("@matchId", matchId);
            return Convert.ToInt32(cmd.ExecuteScalar());
        }
        finally
        {
            if (opened) conn.Dispose();
        }
    }

    public IReadOnlyList<MoveRecord> ListByMatch(string matchId)
    {
        var result = new List<MoveRecord>();
        var conn = GetConnection();
        var opened = EnsureOpen(conn);
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT move_id, match_id, client_move_id, piece_id, from_x, from_y, to_x, to_y,
                       captured_piece_id, board_hash_before, board_hash_after, move_number, result, created_at_utc
                FROM moves WHERE match_id = @matchId
                ORDER BY move_number ASC;";
            cmd.Parameters.AddWithValue("@matchId", matchId);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(MapMove(reader));
            }
        }
        finally
        {
            if (opened) conn.Dispose();
        }
        return result;
    }

    private static MoveRecord MapMove(SqliteDataReader reader)
    {
        return new MoveRecord(
            MoveId: reader.GetString(0),
            MatchId: reader.GetString(1),
            ClientMoveId: reader.GetString(2),
            PieceId: reader.GetString(3),
            From: new Position(reader.GetInt32(4), reader.GetInt32(5)),
            To: new Position(reader.GetInt32(6), reader.GetInt32(7)),
            CapturedPieceId: reader.IsDBNull(8) ? null : reader.GetString(8),
            BoardHashBefore: reader.GetString(9),
            BoardHashAfter: reader.GetString(10),
            MoveNumber: reader.GetInt32(11),
            Result: reader.GetString(12),
            CreatedAtUtc: reader.IsDBNull(13) ? null : DateTime.Parse(reader.GetString(13)));
    }

    private static bool EnsureOpen(SqliteConnection conn)
    {
        if (conn.State == System.Data.ConnectionState.Open)
        {
            return false;
        }
        conn.Open();
        return true;
    }
}
