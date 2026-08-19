using System.Collections.Immutable;
using XiangqiOnline.IntegrationTests.Fixtures;
using XiangqiOnline.Persistence;
using XiangqiOnline.RuleEngine.Adjudication;
using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;

namespace XiangqiOnline.IntegrationTests.Persistence;

public sealed class Tv4Phase2TerminalPersistenceTests : IDisposable
{
    private readonly TestDatabase _db = TestDatabase.Create();

    public void Dispose() => _db.Dispose();

    [Fact]
    public void CheckmatingMove_PersistsOneFinalResultAtFinalRevision()
    {
        var match = _db.Service.CreateMatch(IdGenerator.NewUlid(), "red", "black");
        var board = CheckmateInOneBoard();
        var intent = new MoveIntent(
            IdGenerator.NewUlid(),
            new Position(4, 2),
            new Position(4, 1),
            0);

        var result = _db.Service.CommitMove(match, board, intent);
        var stored = _db.Service.GetMatch(match.MatchId);

        Assert.True(result.IsCommitted);
        Assert.NotNull(result.FinalResult);
        Assert.Equal(GameEndReason.Checkmate, result.FinalResult.EndReason);
        Assert.Equal(1, result.Revision);
        Assert.Equal(1, result.Move!.IsCheckmate);
        Assert.NotNull(stored);
        Assert.Equal("FINISHED", stored.Status);
        Assert.Equal("RED_WIN", stored.ResultType);
        Assert.Equal("CHECKMATE", stored.EndReason);
        Assert.Equal("RED", stored.WinnerSide);
        Assert.Equal(1, stored.FinalRevision);
        Assert.Equal(1, _db.Service.CountMoves(match.MatchId));
    }

    [Fact]
    public void CompetingTerminalResult_IsRejectedAndOriginalRemainsImmutable()
    {
        var match = _db.Service.CreateMatch(IdGenerator.NewUlid(), "red", "black");
        var first = _db.Service.CompleteMatch(
            match.MatchId,
            new GameResult("RED_WIN", GameEndReason.Resignation, SideColor.Red, "Black resigned."),
            0);
        var second = _db.Service.CompleteMatch(
            match.MatchId,
            new GameResult("BLACK_WIN", GameEndReason.Timeout, SideColor.Black, "Red timed out."),
            0);
        var stored = _db.Service.GetMatch(match.MatchId);

        Assert.True(first);
        Assert.False(second);
        Assert.NotNull(stored);
        Assert.Equal("RED_WIN", stored.ResultType);
        Assert.Equal("RESIGNATION", stored.EndReason);
        Assert.Equal("RED", stored.WinnerSide);
    }

    [Fact]
    public void LateMove_AfterMatchEnded_IsRejectedWithoutWritingRows()
    {
        var match = _db.Service.CreateMatch(IdGenerator.NewUlid(), "red", "black");
        Assert.True(_db.Service.CompleteMatch(
            match.MatchId,
            new GameResult("RED_WIN", GameEndReason.Resignation, SideColor.Red, "Black resigned."),
            0));

        var result = _db.Service.CommitMove(
            match,
            BoardState.CreateInitialBoard(),
            new MoveIntent(IdGenerator.NewUlid(), new Position(0, 6), new Position(0, 5), 0));

        Assert.True(result.IsRejected);
        Assert.Equal(ErrorCodes.GAME_NOT_ACTIVE, result.ErrorCode);
        Assert.Equal(0, _db.Service.CountMoves(match.MatchId));
    }

    private static BoardState CheckmateInOneBoard()
    {
        var pieces = new[]
        {
            Piece("BLACK_GENERAL", PieceType.General, SideColor.Black, 4, 0),
            Piece("RED_CHARIOT_LEFT", PieceType.Chariot, SideColor.Red, 3, 1),
            Piece("RED_CHARIOT_MATING", PieceType.Chariot, SideColor.Red, 4, 2),
            Piece("RED_CHARIOT_RIGHT", PieceType.Chariot, SideColor.Red, 5, 1),
            Piece("RED_GENERAL", PieceType.General, SideColor.Red, 4, 9)
        };
        return new BoardState(pieces.ToImmutableDictionary(piece => piece.Position), SideColor.Red);
    }

    private static PieceState Piece(
        string id,
        PieceType type,
        SideColor side,
        int x,
        int y) => new(id, type, side, new Position(x, y));
}
