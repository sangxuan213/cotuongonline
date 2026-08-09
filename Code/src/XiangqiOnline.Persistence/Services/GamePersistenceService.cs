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
    public MatchRecord CreateMatch(string matchId, string? white = null, string? black = null)
    {
        var repo = new MatchRepository(_connectionFactory, _loggerFactory.CreateLogger<MatchRepository>());
        return repo.Create(matchId, white, black);
    }

    /// <summary>Lấy trận đấu.</summary>
    public MatchRecord? GetMatch(string matchId)
    {
        var repo = new MatchRepository(_connectionFactory, _loggerFactory.CreateLogger<MatchRepository>());
        return repo.Get(matchId);
    }

    /// <summary>Commit nước đi (persist-first + atomic).</summary>
    public MoveCommitResult CommitMove(MatchRecord match, BoardState board, MoveIntent intent)
    {
        return _moveCommittingService.Commit(match, board, intent);
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
}
