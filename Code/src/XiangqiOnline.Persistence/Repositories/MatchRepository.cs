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

    /// <summary>Ctor dùng với connection factory (mỗi lệnh mở connection riêng).</summary>
    public MatchRepository(IDbConnectionFactory connectionFactory, ILogger<MatchRepository> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    /// <summary>Ctor dùng chung một connection (trong transaction).</summary>
    public MatchRepository(SqliteConnection connection, ILogger<MatchRepository> logger)
    {
        _externalConnection = connection;
        _logger = logger;
    }

    private SqliteConnection GetConnection()
    {
        return _externalConnection ?? _connectionFactory.CreateConnection();
    }

    private void EnsurePlayerExists(SqliteConnection conn, string? playerId)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return;
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
                INSERT INTO players (player_id, display_name)
                VALUES (@playerId, @displayName)
                ON CONFLICT(player_id) DO NOTHING;";
        cmd.Parameters.AddWithValue("@playerId", playerId);
        cmd.Parameters.AddWithValue("@displayName", playerId);
        cmd.ExecuteNonQuery();
    }

    public MatchRecord Create(string matchId, string? whitePlayerId = null, string? blackPlayerId = null)
    {
        var conn = GetConnection();
        var opened = EnsureOpen(conn);
        try
        {
            EnsurePlayerExists(conn, whitePlayerId);
            EnsurePlayerExists(conn, blackPlayerId);

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO matches (match_id, white_player_id, black_player_id, status, current_turn, revision, board_hash)
                VALUES (@matchId, @white, @black, 'PLAYING', 'RED', 0, '')
                RETURNING match_id, white_player_id, black_player_id, status, current_turn, revision, board_hash, created_at_utc, updated_at_utc;";

            cmd.Parameters.AddWithValue("@matchId", matchId);
            cmd.Parameters.AddWithValue("@white", (object?)whitePlayerId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@black", (object?)blackPlayerId ?? DBNull.Value);

            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
            {
                throw new InvalidOperationException("Không tạo được trận đấu.");
            }

            return MapMatch(reader, whitePlayerId, blackPlayerId);
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
                SELECT match_id, white_player_id, black_player_id, status, current_turn, revision, board_hash, created_at_utc, updated_at_utc
                FROM matches WHERE match_id = @matchId;";
            cmd.Parameters.AddWithValue("@matchId", matchId);

            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
            {
                return null;
            }

            return MapMatch(reader);
        }
        finally
        {
            if (opened) conn.Dispose();
        }
    }

    public void UpdateBoardState(string matchId, string currentTurn, long revision, string boardHash)
    {
        var conn = GetConnection();
        var opened = EnsureOpen(conn);
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE matches
                SET current_turn = @turn, revision = @revision, board_hash = @hash,
                    updated_at_utc = strftime('%Y-%m-%dT%H:%M:%fZ', 'now')
                WHERE match_id = @matchId;";
            cmd.Parameters.AddWithValue("@turn", currentTurn);
            cmd.Parameters.AddWithValue("@revision", revision);
            cmd.Parameters.AddWithValue("@hash", boardHash);
            cmd.Parameters.AddWithValue("@matchId", matchId);
            cmd.ExecuteNonQuery();
        }
        finally
        {
            if (opened) conn.Dispose();
        }
    }

    private static MatchRecord MapMatch(SqliteDataReader reader, string? whiteOverride = null, string? blackOverride = null)
    {
        var matchIdIdx = reader.GetOrdinal("match_id");
        var whiteIdx = reader.GetOrdinal("white_player_id");
        var blackIdx = reader.GetOrdinal("black_player_id");
        var statusIdx = reader.GetOrdinal("status");
        var turnIdx = reader.GetOrdinal("current_turn");
        var revisionIdx = reader.GetOrdinal("revision");
        var hashIdx = reader.GetOrdinal("board_hash");

        var turn = reader.GetString(turnIdx);
        return new MatchRecord(
            MatchId: reader.GetString(matchIdIdx),
            Status: reader.GetString(statusIdx),
            CurrentTurn: turn == "BLACK" ? SideColor.Black : SideColor.Red,
            Revision: reader.GetInt64(revisionIdx),
            BoardHash: reader.GetString(hashIdx),
            WhitePlayerId: whiteOverride ?? (reader.IsDBNull(whiteIdx) ? null : reader.GetString(whiteIdx)),
            BlackPlayerId: blackOverride ?? (reader.IsDBNull(blackIdx) ? null : reader.GetString(blackIdx)));
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
