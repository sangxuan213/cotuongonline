using System.Collections.Immutable;
using System.Diagnostics;
using XiangqiOnline.RuleEngine.Adjudication;
using XiangqiOnline.RuleEngine.LegalMoves;
using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;

namespace XiangqiOnline.RuleEngine.Tests;

public sealed class Phase2AdjudicationTests
{
    [Fact]
    public void InitialBoard_HasFortyFourDeterministicLegalMoves()
    {
        var board = BoardState.CreateInitialBoard();
        var generator = new LegalMoveGenerator();

        var first = generator.Generate(board, SideColor.Red);
        var second = generator.Generate(board, SideColor.Red);

        Assert.Equal(44, first.Count);
        Assert.Equal(first, second);
        Assert.Equal(first.Count, first.Distinct().Count());
    }

    [Fact]
    public void LegalMoveGenerator_BasicBenchmarkCompletesWithinBudget()
    {
        var board = BoardState.CreateInitialBoard();
        var generator = new LegalMoveGenerator();
        var stopwatch = Stopwatch.StartNew();

        for (var index = 0; index < 100; index++)
            Assert.Equal(44, generator.Generate(board, SideColor.Red).Count);

        stopwatch.Stop();
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), stopwatch.Elapsed.ToString());
    }

    [Fact]
    public void LegalMoveGenerator_FiltersMovesThatExposeOwnGeneral()
    {
        var board = CreateBoard(
            SideColor.Red,
            Piece("RED_GENERAL", PieceType.General, SideColor.Red, 4, 9),
            Piece("RED_CHARIOT", PieceType.Chariot, SideColor.Red, 4, 5),
            Piece("BLACK_GENERAL", PieceType.General, SideColor.Black, 4, 0));

        var moves = new LegalMoveGenerator().Generate(board, SideColor.Red);

        Assert.DoesNotContain(moves, move =>
            move.PieceId == "RED_CHARIOT" && move.To.X != 4);
    }

    [Fact]
    public void CheckWithEscape_IsNotTerminal()
    {
        var board = CreateBoard(
            SideColor.Black,
            Piece("BLACK_GENERAL", PieceType.General, SideColor.Black, 4, 0),
            Piece("RED_CHARIOT", PieceType.Chariot, SideColor.Red, 4, 1),
            Piece("RED_GENERAL", PieceType.General, SideColor.Red, 4, 9));

        var result = GameResultResolver.CreateDefault().ResolveBoard(board);
        var moves = new LegalMoveGenerator().Generate(board, SideColor.Black);

        Assert.Null(result);
        Assert.Contains(moves, move => move.To == new Position(3, 0));
        Assert.Contains(moves, move => move.To == new Position(5, 0));
    }

    [Fact]
    public void Checkmate_ProducesWinnerAndCheckmateReason()
    {
        var board = CreateBoard(
            SideColor.Black,
            Piece("BLACK_GENERAL", PieceType.General, SideColor.Black, 4, 0),
            Piece("RED_CHARIOT_LEFT", PieceType.Chariot, SideColor.Red, 3, 1),
            Piece("RED_CHARIOT_CHECK", PieceType.Chariot, SideColor.Red, 4, 1),
            Piece("RED_CHARIOT_RIGHT", PieceType.Chariot, SideColor.Red, 5, 1),
            Piece("RED_GENERAL", PieceType.General, SideColor.Red, 4, 9));

        var result = GameResultResolver.CreateDefault().ResolveBoard(board);

        Assert.NotNull(result);
        Assert.Equal("RED_WIN", result.ResultType);
        Assert.Equal(GameEndReason.Checkmate, result.EndReason);
        Assert.Equal(SideColor.Red, result.WinnerSide);
    }

    [Fact]
    public void NoLegalMoveWithoutCheck_IsLossUnderXiangqiProfile()
    {
        var board = CreateBoard(
            SideColor.Black,
            Piece("BLACK_GENERAL", PieceType.General, SideColor.Black, 4, 0),
            Piece("RED_CHARIOT_LEFT", PieceType.Chariot, SideColor.Red, 3, 1),
            Piece("RED_CHARIOT_RIGHT", PieceType.Chariot, SideColor.Red, 5, 1),
            Piece("RED_BLOCKER", PieceType.Pawn, SideColor.Red, 4, 5),
            Piece("RED_GENERAL", PieceType.General, SideColor.Red, 4, 9));

        var result = GameResultResolver.CreateDefault().ResolveBoard(board);

        Assert.NotNull(result);
        Assert.Equal("RED_WIN", result.ResultType);
        Assert.Equal(GameEndReason.NoLegalMove, result.EndReason);
        Assert.Equal(SideColor.Red, result.WinnerSide);
    }

    [Theory]
    [InlineData(SideColor.Red, "BLACK_WIN", SideColor.Black)]
    [InlineData(SideColor.Black, "RED_WIN", SideColor.Red)]
    public void Timeout_AwardsWinToOpponent(
        SideColor timedOutSide,
        string expectedResult,
        SideColor expectedWinner)
    {
        var result = GameResultResolver.CreateDefault().ResolveTimeout(timedOutSide);

        Assert.Equal(expectedResult, result.ResultType);
        Assert.Equal(GameEndReason.Timeout, result.EndReason);
        Assert.Equal(expectedWinner, result.WinnerSide);
    }

    [Fact]
    public void DrawAgreement_HasNoWinner()
    {
        var result = GameResultResolver.CreateDefault().ResolveDrawAgreement();

        Assert.Equal("DRAW", result.ResultType);
        Assert.Equal(GameEndReason.DrawAgreement, result.EndReason);
        Assert.Null(result.WinnerSide);
    }

    [Fact]
    public void CompetingTerminalSignals_UseDeterministicPriority()
    {
        var resolver = GameResultResolver.CreateDefault();
        var selected = resolver.ResolveByPriority(new[]
        {
            resolver.ResolveDrawAgreement(),
            resolver.ResolveTimeout(SideColor.Red),
            resolver.ResolveResignation(SideColor.Black),
            new GameResult("RED_WIN", GameEndReason.Checkmate, SideColor.Red, "Mate.")
        });

        Assert.Equal(GameEndReason.Checkmate, selected.EndReason);
        Assert.Equal("RED_WIN", selected.ResultType);
    }

    private static BoardState CreateBoard(SideColor turn, params PieceState[] pieces) =>
        new(pieces.ToImmutableDictionary(piece => piece.Position), turn);

    private static PieceState Piece(
        string id,
        PieceType type,
        SideColor side,
        int x,
        int y) => new(id, type, side, new Position(x, y));
}
