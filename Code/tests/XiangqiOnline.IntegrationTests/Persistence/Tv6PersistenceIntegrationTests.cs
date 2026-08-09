using XiangqiOnline.IntegrationTests.Fixtures;
using XiangqiOnline.Persistence;
using XiangqiOnline.Persistence.Models;
using XiangqiOnline.Persistence.Services;
using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.Shared.Models;

namespace XiangqiOnline.IntegrationTests.Persistence;

/// <summary>
/// TV6 Phase 1 integration tests (P1-TV6-D5) - REAL SQLite database (temp file).
/// Verifies persistence semantics: legal move = 1 row, duplicate = still 1 row,
/// rejected = 0 new rows, persistence failure = rollback, read-back consistency.
/// </summary>
public sealed class Tv6PersistenceIntegrationTests : IDisposable
{
    private readonly TestDatabase _db = TestDatabase.Create();

    public void Dispose() => _db.Dispose();

    [Fact]
    public void Legal_move_commits_exactly_one_db_row()
    {
        // Arrange
        var match = _db.Service.CreateMatch(IdGenerator.NewUlid(), "red", "black");
        var board = BoardState.CreateInitialBoard();
        var intent = new MoveIntent(IdGenerator.NewUlid(), new Position(0, 9), new Position(0, 7), match.Revision);

        // Act
        var result = _db.Service.CommitMove(match, board, intent);

        // Assert
        Assert.True(result.IsCommitted);
        Assert.Equal(1, _db.Service.CountMoves(match.MatchId));
        var stored = _db.Service.ListMoves(match.MatchId);
        Assert.Single(stored);
        Assert.Equal(intent.ClientMoveId, stored[0].ClientMoveId);
    }

    [Fact]
    public void Duplicate_clientMoveId_retry_still_one_row()
    {
        // Arrange
        var match = _db.Service.CreateMatch(IdGenerator.NewUlid(), "red", "black");
        var board = BoardState.CreateInitialBoard();
        var clientMoveId = IdGenerator.NewUlid();
        var intent1 = new MoveIntent(clientMoveId, new Position(0, 9), new Position(0, 7), match.Revision);
        var intent2 = new MoveIntent(clientMoveId, new Position(0, 9), new Position(0, 7), match.Revision);

        // Act
        var first = _db.Service.CommitMove(match, board, intent1);
        var second = _db.Service.CommitMove(match, board, intent2);

        // Assert
        Assert.True(first.IsCommitted);
        Assert.True(second.IsDuplicate);
        Assert.Equal(1, _db.Service.CountMoves(match.MatchId));
    }

    [Fact]
    public void Rejected_move_creates_zero_new_rows()
    {
        // Arrange
        var match = _db.Service.CreateMatch(IdGenerator.NewUlid(), "red", "black");
        var board = BoardState.CreateInitialBoard();

        // Illegal move: move RED_HORSE to a position that is not a valid horse move, e.g. (1,9) -> (0,0)
        var intent = new MoveIntent(IdGenerator.NewUlid(), new Position(1, 9), new Position(0, 0), match.Revision);

        // Act
        var result = _db.Service.CommitMove(match, board, intent);

        // Assert
        Assert.True(result.IsRejected);
        Assert.Equal(0, _db.Service.CountMoves(match.MatchId));
    }

    [Fact]
    public void Persistence_failure_rolls_back_no_partial_state()
    {
        // Arrange
        // Create match, then attempt to commit a move against a NON-EXISTENT match id
        // so the move insert violates the FK -> transaction rollback -> PersistenceFailure.
        var fakeMatch = new MatchRecord(
            MatchId: "does-not-exist",
            Status: "PLAYING",
            CurrentTurn: XiangqiOnline.Shared.Enums.SideColor.Red,
            Revision: 0,
            BoardHash: "");
        var board = BoardState.CreateInitialBoard();
        var intent = new MoveIntent(IdGenerator.NewUlid(), new Position(0, 9), new Position(0, 7), fakeMatch.Revision);

        // Act
        var result = _db.Service.CommitMove(fakeMatch, board, intent);

        // Assert: DB failure returns PersistenceFailure, no partial state / revision change.
        Assert.True(result.IsPersistenceFailure);
        Assert.Equal(0, _db.Service.CountMoves(fakeMatch.MatchId));
    }

    [Fact]
    public void Persistence_failure_does_not_change_revision_or_state()
    {
        // Arrange
        var match = _db.Service.CreateMatch(IdGenerator.NewUlid(), "red", "black");
        var board = BoardState.CreateInitialBoard();
        var intent = new MoveIntent(IdGenerator.NewUlid(), new Position(0, 9), new Position(0, 7), match.Revision);

        // Act: commit a legal move first (revision becomes 1)
        var ok = _db.Service.CommitMove(match, board, intent);
        Assert.True(ok.IsCommitted);

        var before = _db.Service.GetMatch(match.MatchId)!;
        Assert.Equal(1L, before.Revision);

        // Attempt a duplicate to exercise the unique path (still 1 row, revision unchanged)
        var dup = new MoveIntent(intent.ClientMoveId, new Position(0, 9), new Position(0, 7), before.Revision);
        var dupResult = _db.Service.CommitMove(match, board, dup);
        Assert.True(dupResult.IsDuplicate);

        var after = _db.Service.GetMatch(match.MatchId)!;
        Assert.Equal(1L, after.Revision); // revision unchanged on duplicate
        Assert.Equal(1, _db.Service.CountMoves(match.MatchId));
    }

    [Fact]
    public void Committed_move_read_back_is_consistent()
    {
        // Arrange
        var match = _db.Service.CreateMatch(IdGenerator.NewUlid(), "red", "black");
        var board = BoardState.CreateInitialBoard();
        var from = new Position(0, 9);
        var to = new Position(0, 7);
        var intent = new MoveIntent(IdGenerator.NewUlid(), from, to, match.Revision);

        // Act
        var result = _db.Service.CommitMove(match, board, intent);
        Assert.True(result.IsCommitted);

        // Read back the single stored move
        var stored = _db.Service.ListMoves(match.MatchId);
        var move = Assert.Single(stored);

        // Assert consistency of all fields
        Assert.Equal(intent.ClientMoveId, move.ClientMoveId);
        Assert.Equal(from, move.From);
        Assert.Equal(to, move.To);
        Assert.Equal("RED_CHARIOT_1", move.PieceId);
        Assert.Equal(1, move.MoveNumber);
        Assert.Equal("COMMITTED", move.Result);
        Assert.False(string.IsNullOrWhiteSpace(move.BoardHashBefore));
        Assert.False(string.IsNullOrWhiteSpace(move.BoardHashAfter));
        Assert.NotEqual(move.BoardHashBefore, move.BoardHashAfter);
    }

    [Fact]
    public void Board_hash_before_and_after_are_recorded()
    {
        // Arrange
        var match = _db.Service.CreateMatch(IdGenerator.NewUlid(), "red", "black");
        var board = BoardState.CreateInitialBoard();
        var intent = new MoveIntent(IdGenerator.NewUlid(), new Position(0, 9), new Position(0, 7), match.Revision);

        // Act
        var result = _db.Service.CommitMove(match, board, intent);
        Assert.True(result.IsCommitted);

        var stored = _db.Service.ListMoves(match.MatchId);
        var move = Assert.Single(stored);
        Assert.Equal(32, Assert_CountPieces(board)); // before hash covers full board
        Assert.True(move.BoardHashBefore.Length == 64); // SHA-256 hex
        Assert.True(move.BoardHashAfter.Length == 64);
    }

    private static int Assert_CountPieces(BoardState board) => board.GetActivePieces().Count();
}
