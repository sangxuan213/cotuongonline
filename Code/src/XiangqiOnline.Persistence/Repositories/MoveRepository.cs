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

    private SqliteConnection GetConnection() => _externalConnection ?? _connectionFactory.CreateConnection();

    public bool TryInsert(MoveRecord move)
    {
        var conn = GetConnection();
        var opened = EnsureOpen(conn);
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO moves
                    (move_id, client_move_id, match_id, move_index, revision, side,
                     piece_id, piece_type, from_x, from_y, to_x, to_y, captured_piece_id,
                     move_class, classification_facts_json, is_capture, is_check, is_checkmate,
                     red_remaining_ms, black_remaining_ms, board_hash_before, board_hash_after, created_at_utc)
                VALUES
                    (@moveId, @clientMoveId, @matchId, @moveIndex, @revision, @side,
                     @pieceId, @pieceType, @fromX, @fromY, @toX, @toY, @captured,
                     @moveClass, @factsJson, @isCapture, @isCheck, @isCheckmate,
                     @redMs, @blackMs, @hashBefore, @hashAfter, @createdAt);";

            cmd.Parameters.AddWithValue("@moveId", move.MoveId);
            cmd.Parameters.AddWithValue("@clientMoveId", move.ClientMoveId);
            cmd.Parameters.AddWithValue("@matchId", move.MatchId);
            cmd.Parameters.AddWithValue("@moveIndex", move.MoveIndex);
            cmd.Parameters.AddWithValue("@revision", move.Revision);
            cmd.Parameters.AddWithValue("@side", move.Side);
            cmd.Parameters.AddWithValue("@pieceId", move.PieceId);
            cmd.Parameters.AddWithValue("@pieceType", move.PieceType);
            cmd.Parameters.AddWithValue("@fromX", move.From.X);
            cmd.Parameters.AddWithValue("@fromY", move.From.Y);
            cmd.Parameters.AddWithValue("@toX", move.To.X);
            cmd.Parameters.AddWithValue("@toY", move.To.Y);
            cmd.Parameters.AddWithValue("@captured", (object?)move.CapturedPieceId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@moveClass", move.MoveClass);
            cmd.Parameters.AddWithValue("@factsJson", move.ClassificationFactsJson ?? "{}");
            cmd.Parameters.AddWithValue("@isCapture", move.IsCapture);
            cmd.Parameters.AddWithValue("@isCheck", move.IsCheck);
            cmd.Parameters.AddWithValue("@isCheckmate", move.IsCheckmate);
            cmd.Parameters.AddWithValue("@redMs", move.RedRemainingMs);
            cmd.Parameters.AddWithValue("@blackMs", move.BlackRemainingMs);
            cmd.Parameters.AddWithValue("@hashBefore", move.BoardHashBefore);
            cmd.Parameters.AddWithValue("@hashAfter", move.BoardHashAfter);
            cmd.Parameters.AddWithValue("@createdAt", move.CreatedAtUtc?.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") ?? DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));

            cmd.ExecuteNonQuery();
            return true;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19) // SQLITE_CONSTRAINT
        {
            if (ex.Message.Contains("FOREIGN KEY", StringComparison.OrdinalIgnoreCase) || ex.SqliteExtendedErrorCode == 787)
            {
                throw; // Rethrow FK violation so service catches it as PersistenceFailure
            }
            _logger.LogWarning("Constraint violation on move insert. matchId={MatchId} clientMoveId={ClientMoveId} error={Error}",
                move.MatchId, move.ClientMoveId, ex.Message);
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
                SELECT move_id, client_move_id, match_id, move_index, revision, side,
                       piece_id, piece_type, from_x, from_y, to_x, to_y, captured_piece_id,
                       move_class, classification_facts_json, is_capture, is_check, is_checkmate,
                       red_remaining_ms, black_remaining_ms, board_hash_before, board_hash_after, created_at_utc
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
                SELECT move_id, client_move_id, match_id, move_index, revision, side,
                       piece_id, piece_type, from_x, from_y, to_x, to_y, captured_piece_id,
                       move_class, classification_facts_json, is_capture, is_check, is_checkmate,
                       red_remaining_ms, black_remaining_ms, board_hash_before, board_hash_after, created_at_utc
                FROM moves WHERE match_id = @matchId
                ORDER BY move_index ASC;";
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
            ClientMoveId: reader.GetString(1),
            MatchId: reader.GetString(2),
            MoveIndex: reader.GetInt32(3),
            Revision: reader.GetInt64(4),
            Side: reader.GetString(5),
            PieceId: reader.GetString(6),
            PieceType: reader.GetString(7),
            From: new Position(reader.GetInt32(8), reader.GetInt32(9)),
            To: new Position(reader.GetInt32(10), reader.GetInt32(11)),
            CapturedPieceId: reader.IsDBNull(12) ? null : reader.GetString(12),
            MoveClass: reader.GetString(13),
            ClassificationFactsJson: reader.GetString(14),
            IsCapture: reader.GetInt32(15),
            IsCheck: reader.GetInt32(16),
            IsCheckmate: reader.GetInt32(17),
            RedRemainingMs: reader.GetInt32(18),
            BlackRemainingMs: reader.GetInt32(19),
            BoardHashBefore: reader.GetString(20),
            BoardHashAfter: reader.GetString(21),
            CreatedAtUtc: reader.IsDBNull(22) ? null : DateTime.Parse(reader.GetString(22))
        );
    }

    private static bool EnsureOpen(SqliteConnection conn)
    {
        if (conn.State == System.Data.ConnectionState.Open) return false;
        conn.Open();
        return true;
    }
}
