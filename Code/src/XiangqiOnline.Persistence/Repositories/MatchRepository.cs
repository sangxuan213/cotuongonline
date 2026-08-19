using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using XiangqiOnline.Persistence.Database;
using XiangqiOnline.Persistence.Models;
using XiangqiOnline.Shared.Enums;

namespace XiangqiOnline.Persistence.Repositories;

/// <summary>
/// Repository cho bảng <c>matches</c>. Tất cả SQL đều dùng parameterized parameters.
/// Khi tạo match với player id, đảm bảo player row tồn tại trong bảng <c>players</c>
/// (upsert) để không vi phạm foreign key.
/// </summary>
public sealed class MatchRepository : IMatchRepository
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly SqliteConnection? _externalConnection;
    private readonly ILogger<MatchRepository> _logger;

    public MatchRepository(IDbConnectionFactory connectionFactory, ILogger<MatchRepository> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public MatchRepository(SqliteConnection connection, ILogger<MatchRepository> logger)
    {
        _externalConnection = connection;
        _logger = logger;
    }

    private SqliteConnection GetConnection() => _externalConnection ?? _connectionFactory.CreateConnection();

    private static void EnsurePlayerExists(SqliteConnection conn, string playerId)
    {
        if (string.IsNullOrWhiteSpace(playerId)) return;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO players (player_id, display_name, created_at_utc)
            VALUES (@playerId, @displayName, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'))
            ON CONFLICT(player_id) DO NOTHING;";
        cmd.Parameters.AddWithValue("@playerId", playerId);
        cmd.Parameters.AddWithValue("@displayName", playerId);
        cmd.ExecuteNonQuery();
    }

    public MatchRecord Create(
        string matchId,
        string roomId,
        string redPlayerId,
        string blackPlayerId,
        string ruleProfileId = "UDM18_WXF_PRO_2018",
        string ruleProfileVersion = "1.1",
        string timeProfile = "STANDARD",
        string configJson = "{}")
    {
        var conn = GetConnection();
        var opened = EnsureOpen(conn);
        try
        {
            EnsurePlayerExists(conn, redPlayerId);
            EnsurePlayerExists(conn, blackPlayerId);

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO matches
                    (match_id, room_id, red_player_id, black_player_id, rule_profile_id,
                     rule_profile_version, time_profile, config_json, status, started_at_utc, total_moves)
                VALUES
                    (@matchId, @roomId, @red, @black, @ruleProfile,
                     @ruleVersion, @timeProfile, @config, 'PLAYING', strftime('%Y-%m-%dT%H:%M:%fZ', 'now'), 0)
                RETURNING match_id, room_id, red_player_id, black_player_id, rule_profile_id,
                          rule_profile_version, time_profile, config_json, status, started_at_utc,
                          ended_at_utc, result_type, end_reason, winner_side, final_revision, total_moves;";

            cmd.Parameters.AddWithValue("@matchId", matchId);
            cmd.Parameters.AddWithValue("@roomId", roomId);
            cmd.Parameters.AddWithValue("@red", redPlayerId);
            cmd.Parameters.AddWithValue("@black", blackPlayerId);
            cmd.Parameters.AddWithValue("@ruleProfile", ruleProfileId);
            cmd.Parameters.AddWithValue("@ruleVersion", ruleProfileVersion);
            cmd.Parameters.AddWithValue("@timeProfile", timeProfile);
            cmd.Parameters.AddWithValue("@config", configJson);

            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
            {
                throw new InvalidOperationException("Không tạo được trận đấu.");
            }

            return MapMatch(reader);
        }
        finally
        {
            if (opened) conn.Dispose();
        }
    }

    public MatchRecord? Get(string matchId)
    {
        var conn = GetConnection();
        var opened = EnsureOpen(conn);
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT match_id, room_id, red_player_id, black_player_id, rule_profile_id,
                       rule_profile_version, time_profile, config_json, status, started_at_utc,
                       ended_at_utc, result_type, end_reason, winner_side, final_revision, total_moves
                FROM matches WHERE match_id = @matchId;";
            cmd.Parameters.AddWithValue("@matchId", matchId);

            using var reader = cmd.ExecuteReader();
            return reader.Read() ? MapMatch(reader) : null;
        }
        finally
        {
            if (opened) conn.Dispose();
        }
    }

    public void UpdateBoardState(string matchId, long revision, int totalMoves)
    {
        var conn = GetConnection();
        var opened = EnsureOpen(conn);
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE matches
                SET total_moves = @totalMoves,
                    final_revision = @revision
                WHERE match_id = @matchId;";
            cmd.Parameters.AddWithValue("@revision", revision);
            cmd.Parameters.AddWithValue("@totalMoves", totalMoves);
            cmd.Parameters.AddWithValue("@matchId", matchId);
            cmd.ExecuteNonQuery();
        }
        finally
        {
            if (opened) conn.Dispose();
        }
    }

    public bool Complete(
        string matchId,
        string resultType,
        string endReason,
        string? winnerSide,
        long finalRevision,
        DateTime endedAtUtc)
    {
        var conn = GetConnection();
        var opened = EnsureOpen(conn);
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE matches
                SET status = 'FINISHED',
                    ended_at_utc = @endedAtUtc,
                    result_type = @resultType,
                    end_reason = @endReason,
                    winner_side = @winnerSide,
                    final_revision = @finalRevision
                WHERE match_id = @matchId
                  AND status = 'PLAYING';";
            cmd.Parameters.AddWithValue("@endedAtUtc", endedAtUtc.ToUniversalTime().ToString("O"));
            cmd.Parameters.AddWithValue("@resultType", resultType);
            cmd.Parameters.AddWithValue("@endReason", endReason);
            cmd.Parameters.AddWithValue("@winnerSide", (object?)winnerSide ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@finalRevision", finalRevision);
            cmd.Parameters.AddWithValue("@matchId", matchId);
            return cmd.ExecuteNonQuery() == 1;
        }
        finally
        {
            if (opened) conn.Dispose();
        }
    }

    private static MatchRecord MapMatch(SqliteDataReader reader)
    {
        return new MatchRecord(
            MatchId: reader.GetString(0),
            RoomId: reader.GetString(1),
            RedPlayerId: reader.GetString(2),
            BlackPlayerId: reader.GetString(3),
            RuleProfileId: reader.GetString(4),
            RuleProfileVersion: reader.GetString(5),
            TimeProfile: reader.GetString(6),
            ConfigJson: reader.GetString(7),
            Status: reader.GetString(8),
            StartedAtUtc: DateTime.Parse(reader.GetString(9)),
            EndedAtUtc: reader.IsDBNull(10) ? null : DateTime.Parse(reader.GetString(10)),
            ResultType: reader.IsDBNull(11) ? null : reader.GetString(11),
            EndReason: reader.IsDBNull(12) ? null : reader.GetString(12),
            WinnerSide: reader.IsDBNull(13) ? null : reader.GetString(13),
            FinalRevision: reader.IsDBNull(14) ? null : reader.GetInt64(14),
            TotalMoves: reader.GetInt32(15)
        );
    }

    private static bool EnsureOpen(SqliteConnection conn)
    {
        if (conn.State == System.Data.ConnectionState.Open) return false;
        conn.Open();
        return true;
    }
}
