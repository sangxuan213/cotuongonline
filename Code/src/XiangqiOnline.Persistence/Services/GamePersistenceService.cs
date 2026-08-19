using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using XiangqiOnline.Persistence.Configuration;
using XiangqiOnline.Persistence.Database;
using XiangqiOnline.Persistence.Models;
using XiangqiOnline.Persistence.Repositories;
using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.RuleEngine.Pipeline;
using XiangqiOnline.Shared.Models;

namespace XiangqiOnline.Persistence.Services;

/// <summary>
/// Facade cho GamePersistence: khởi tạo DB, tạo match, commit nước đi.
/// Đây là API chính mà Server / tests dùng.
/// </summary>
public sealed class GamePersistenceService
{
    private readonly DatabaseOptions _options;
    private readonly ILoggerFactory _loggerFactory;
    private readonly MoveCommittingService _moveCommittingService;
    private readonly IDbConnectionFactory _connectionFactory;

    public GamePersistenceService(
        DatabaseOptions options,
        ILoggerFactory loggerFactory,
        MoveValidationPipeline? pipeline = null)
    {
        _options = options;
        _loggerFactory = loggerFactory;
        _connectionFactory = new DbConnectionFactory(options);
        _moveCommittingService = new MoveCommittingService(
            options,
            pipeline ?? new MoveValidationPipeline(),
            loggerFactory.CreateLogger<MoveCommittingService>());
    }

    /// <summary>Khởi tạo database và schema (idempotent).</summary>
    public void InitializeDatabase()
    {
        var initializer = new DatabaseInitializer(_options, _loggerFactory.CreateLogger<DatabaseInitializer>());
        initializer.Initialize();
    }

    /// <summary>Khởi tạo database trên connection đã mở (dùng trong tests).</summary>
    public static void InitializeDatabase(SqliteConnection connection)
    {
        DatabaseInitializer.ApplySchema(connection);
    }

    /// <summary>Tạo trận đấu mới.</summary>
    public MatchRecord CreateMatch(
        string matchId,
        string? redPlayerId = null,
        string? blackPlayerId = null,
        string? roomId = null,
        string? ruleProfileId = null,
        string? timeProfile = null)
    {
        var red = string.IsNullOrWhiteSpace(redPlayerId) ? "red-player" : redPlayerId;
        var black = string.IsNullOrWhiteSpace(blackPlayerId) ? "black-player" : blackPlayerId;
        var room = string.IsNullOrWhiteSpace(roomId) ? "room-" + matchId : roomId;

        var repo = new MatchRepository(_connectionFactory, _loggerFactory.CreateLogger<MatchRepository>());
        return repo.Create(
            matchId,
            room,
            red,
            black,
            ruleProfileId ?? "UDM18_WXF_PRO_2018",
            "1.1",
            timeProfile ?? "STANDARD");
    }

    /// <summary>Lấy trận đấu.</summary>
    public MatchRecord? GetMatch(string matchId)
    {
        var repo = new MatchRepository(_connectionFactory, _loggerFactory.CreateLogger<MatchRepository>());
        return repo.Get(matchId);
    }

    /// <summary>Commit nước đi (persist-first + atomic).</summary>
    public MoveCommitResult CommitMove(
        MatchRecord match,
        BoardState board,
        MoveIntent intent,
        int redRemainingMs = 600000,
        int blackRemainingMs = 600000)
    {
        return _moveCommittingService.Commit(match, board, intent, redRemainingMs, blackRemainingMs);
    }

    /// <summary>Đếm số nước đi của trận.</summary>
    public int CountMoves(string matchId)
    {
        var repo = new MoveRepository(_connectionFactory, _loggerFactory.CreateLogger<MoveRepository>());
        return repo.CountByMatch(matchId);
    }

    /// <summary>Lấy danh sách nước đi của trận.</summary>
    public IReadOnlyList<MoveRecord> ListMoves(string matchId)
    {
        var repo = new MoveRepository(_connectionFactory, _loggerFactory.CreateLogger<MoveRepository>());
        return repo.ListByMatch(matchId);
    }

    /// <summary>Lấy lịch sử vị trí của trận.</summary>
    public IReadOnlyList<PositionHistoryRecord> ListPositionHistory(string matchId)
    {
        var repo = new PositionHistoryRepository(_connectionFactory, _loggerFactory.CreateLogger<PositionHistoryRepository>());
        return repo.ListByMatch(matchId);
    }

    public void CompleteMatch(
        string matchId,
        string resultType,
        string endReason,
        string? winnerSide,
        long finalRevision,
        DateTime endedAtUtc)
    {
        var repo = new MatchRepository(_connectionFactory, _loggerFactory.CreateLogger<MatchRepository>());
        repo.Complete(matchId, resultType, endReason, winnerSide, finalRevision, endedAtUtc);
    }

    public IReadOnlyList<MatchRecord> ListMatchesByPlayer(string playerId, int limit = 100)
    {
        var repo = new MatchRepository(_connectionFactory, _loggerFactory.CreateLogger<MatchRepository>());
        return repo.ListByPlayer(playerId, limit);
    }

    public string ResolvePlayerDisplayName(string playerId)
    {
        if (string.IsNullOrWhiteSpace(playerId)) return "Kỳ thủ";
        using var connection = _connectionFactory.CreateConnection();
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT display_name FROM accounts WHERE account_id = @accountId
            UNION ALL
            SELECT display_name FROM players WHERE player_id = @playerId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@accountId", playerId.StartsWith("ACCOUNT_", StringComparison.OrdinalIgnoreCase)
            ? playerId["ACCOUNT_".Length..]
            : playerId);
        command.Parameters.AddWithValue("@playerId", playerId);
        return command.ExecuteScalar()?.ToString() ?? (playerId.StartsWith("BOT_", StringComparison.OrdinalIgnoreCase) ? "Máy tính" : "Kỳ thủ");
    }
}
