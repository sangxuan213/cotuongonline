using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using XiangqiOnline.Persistence.Database;
using XiangqiOnline.Persistence.Models;

namespace XiangqiOnline.Persistence.Repositories;

/// <summary>
/// Repository cho bảng <c>position_history</c>. Parameterized SQL.
/// </summary>
public sealed class PositionHistoryRepository : IPositionHistoryRepository
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly SqliteConnection? _externalConnection;
    private readonly ILogger<PositionHistoryRepository> _logger;

    public PositionHistoryRepository(IDbConnectionFactory connectionFactory, ILogger<PositionHistoryRepository> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public PositionHistoryRepository(SqliteConnection connection, ILogger<PositionHistoryRepository> logger)
    {
        _connectionFactory = null!;
        _externalConnection = connection;
        _logger = logger;
    }

    private SqliteConnection GetConnection() => _externalConnection ?? _connectionFactory.CreateConnection();

    public void Insert(PositionHistoryRecord record)
    {
        var conn = GetConnection();
        var opened = EnsureOpen(conn);
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO position_history
                    (match_id, revision, board_hash, canonical_piece_map_json, side_to_move,
                     move_class, classification_facts_json, cycle_signature, must_vary_side, adjudication_reason, created_at_utc)
                VALUES
                    (@matchId, @revision, @boardHash, @mapJson, @sideToMove,
                     @moveClass, @factsJson, @cycleSig, @mustVary, @adjReason, @createdAt);";

            cmd.Parameters.AddWithValue("@matchId", record.MatchId);
            cmd.Parameters.AddWithValue("@revision", record.Revision);
            cmd.Parameters.AddWithValue("@boardHash", record.BoardHash);
            cmd.Parameters.AddWithValue("@mapJson", record.CanonicalPieceMapJson);
            cmd.Parameters.AddWithValue("@sideToMove", record.SideToMove);
            cmd.Parameters.AddWithValue("@moveClass", (object?)record.MoveClass ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@factsJson", record.ClassificationFactsJson ?? "{}");
            cmd.Parameters.AddWithValue("@cycleSig", (object?)record.CycleSignature ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@mustVary", (object?)record.MustVarySide ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@adjReason", (object?)record.AdjudicationReason ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@createdAt", record.CreatedAtUtc?.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") ?? DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));

            cmd.ExecuteNonQuery();
        }
        finally
        {
            if (opened) conn.Dispose();
        }
    }

    public IReadOnlyList<PositionHistoryRecord> ListByMatch(string matchId)
    {
        var result = new List<PositionHistoryRecord>();
        var conn = GetConnection();
        var opened = EnsureOpen(conn);
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT match_id, revision, board_hash, canonical_piece_map_json, side_to_move,
                       move_class, classification_facts_json, cycle_signature, must_vary_side, adjudication_reason, created_at_utc
                FROM position_history WHERE match_id = @matchId
                ORDER BY revision ASC;";
            cmd.Parameters.AddWithValue("@matchId", matchId);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new PositionHistoryRecord(
                    MatchId: reader.GetString(0),
                    Revision: reader.GetInt64(1),
                    BoardHash: reader.GetString(2),
                    CanonicalPieceMapJson: reader.GetString(3),
                    SideToMove: reader.GetString(4),
                    MoveClass: reader.IsDBNull(5) ? null : reader.GetString(5),
                    ClassificationFactsJson: reader.GetString(6),
                    CycleSignature: reader.IsDBNull(7) ? null : reader.GetString(7),
                    MustVarySide: reader.IsDBNull(8) ? null : reader.GetString(8),
                    AdjudicationReason: reader.IsDBNull(9) ? null : reader.GetString(9),
                    CreatedAtUtc: reader.IsDBNull(10) ? null : DateTime.Parse(reader.GetString(10))
                ));
            }
        }
        finally
        {
            if (opened) conn.Dispose();
        }
        return result;
    }

    private static bool EnsureOpen(SqliteConnection conn)
    {
        if (conn.State == System.Data.ConnectionState.Open) return false;
        conn.Open();
        return true;
    }
}
