using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using XiangqiOnline.IntegrationTests.Fixtures;
using XiangqiOnline.Persistence.Database;
using XiangqiOnline.Persistence.Models;
using XiangqiOnline.Persistence.Repositories;
using XiangqiOnline.Shared.Models;

namespace XiangqiOnline.IntegrationTests.Persistence;

/// <summary>
/// TV6 Phase 1 repository tests (P1-TV6-D2):
/// - parameterized SQL (SQL injection safe)
/// - transaction / disposal
/// - unique constraint enforcement via repository
/// </summary>
public sealed class Tv6RepositoryTests : IDisposable
{
    private readonly TestDatabase _db = TestDatabase.Create();
    private readonly MoveRepository _moveRepo;
    private readonly MatchRepository _matchRepo;

    public Tv6RepositoryTests()
    {
        var factory = new DbConnectionFactory(_db.Options);
        _moveRepo = new MoveRepository(factory, NullLogger<MoveRepository>.Instance);
        _matchRepo = new MatchRepository(factory, NullLogger<MatchRepository>.Instance);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public void Parameterized_sql_prevents_injection()
    {
        var match = _matchRepo.Create("m-inject", "r-inject", "red", "black");
        var maliciousClientMoveId = "'; DROP TABLE moves; --";

        // This should be treated literally as a value, not executed as SQL.
        var move = new MoveRecord(
            MoveId: "mv-inject",
            ClientMoveId: maliciousClientMoveId,
            MatchId: match.MatchId,
            MoveIndex: 1,
            Revision: 1,
            Side: "RED",
            PieceId: "P1",
            PieceType: "PAWN",
            From: new Position(0, 0),
            To: new Position(0, 1),
            CapturedPieceId: null,
            MoveClass: "IDLE",
            ClassificationFactsJson: "{}",
            IsCapture: 0,
            IsCheck: 0,
            IsCheckmate: 0,
            RedRemainingMs: 600000,
            BlackRemainingMs: 600000,
            BoardHashBefore: "h1",
            BoardHashAfter: "h2",
            CreatedAtUtc: DateTime.UtcNow);

        var inserted = _moveRepo.TryInsert(move);
        Assert.True(inserted);

        // The table must still exist (injection did not drop it).
        Assert.True(TableExists("moves"));

        // And the malicious value was stored literally.
        var stored = _moveRepo.GetByClientMoveId(match.MatchId, maliciousClientMoveId);
        Assert.NotNull(stored);
        Assert.Equal(maliciousClientMoveId, stored.ClientMoveId);
    }

    [Fact]
    public void Repository_uses_transaction_and_disposes_cleanly()
    {
        var match = _matchRepo.Create("m-tx", "r-tx", "red", "black");

        using (var conn = new SqliteConnection(_db.Options.BuildConnectionString()))
        {
            conn.Open();
            using var tx = conn.BeginTransaction();
            var repo = new MoveRepository(conn, NullLogger<MoveRepository>.Instance);
            var move = new MoveRecord(
                MoveId: "mv-tx",
                ClientMoveId: "c-tx",
                MatchId: match.MatchId,
                MoveIndex: 1,
                Revision: 1,
                Side: "RED",
                PieceId: "P1",
                PieceType: "PAWN",
                From: new Position(0, 0),
                To: new Position(0, 1),
                CapturedPieceId: null,
                MoveClass: "IDLE",
                ClassificationFactsJson: "{}",
                IsCapture: 0,
                IsCheck: 0,
                IsCheckmate: 0,
                RedRemainingMs: 600000,
                BlackRemainingMs: 600000,
                BoardHashBefore: "h1",
                BoardHashAfter: "h2",
                CreatedAtUtc: DateTime.UtcNow);
            var inserted = repo.TryInsert(move);
            Assert.True(inserted);
            tx.Commit();
        }

        // After commit and disposal, the row is visible through a new connection.
        Assert.Equal(1, _moveRepo.CountByMatch(match.MatchId));
    }

    [Fact]
    public void Repository_rejects_duplicate_client_move()
    {
        var match = _matchRepo.Create("m-dup", "r-dup", "red", "black");
        var makeMove = (string id, string clientMoveId) => new MoveRecord(
            MoveId: id,
            ClientMoveId: clientMoveId,
            MatchId: match.MatchId,
            MoveIndex: id == "a" ? 1 : 2,
            Revision: id == "a" ? 1 : 2,
            Side: "RED",
            PieceId: "P1",
            PieceType: "PAWN",
            From: new Position(0, 0),
            To: new Position(0, 1),
            CapturedPieceId: null,
            MoveClass: "IDLE",
            ClassificationFactsJson: "{}",
            IsCapture: 0,
            IsCheck: 0,
            IsCheckmate: 0,
            RedRemainingMs: 600000,
            BlackRemainingMs: 600000,
            BoardHashBefore: "h1",
            BoardHashAfter: "h2",
            CreatedAtUtc: DateTime.UtcNow);

        Assert.True(_moveRepo.TryInsert(makeMove("a", "same-id")));
        Assert.False(_moveRepo.TryInsert(makeMove("b", "same-id")));
        Assert.Equal(1, _moveRepo.CountByMatch(match.MatchId));
    }

    private bool TableExists(string table)
    {
        using var conn = new SqliteConnection(_db.Options.BuildConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@name;";
        cmd.Parameters.AddWithValue("@name", table);
        return Convert.ToInt32(cmd.ExecuteScalar()) == 1;
    }
}
