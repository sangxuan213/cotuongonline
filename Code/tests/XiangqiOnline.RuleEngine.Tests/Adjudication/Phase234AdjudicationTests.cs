using XiangqiOnline.RuleEngine.Adjudication;
using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;

namespace XiangqiOnline.RuleEngine.Tests.Adjudication;

public sealed class Phase234AdjudicationTests
{
    [Fact]
    public void LegalMoveGenerator_IsDeterministicAndDoesNotMutateInput()
    {
        var board = BoardState.CreateInitialBoard();
        var before = BoardFingerprint.Canonical(board);
        var generator = new LegalMoveGenerator();

        var first = generator.Generate(board, SideColor.Red);
        var second = generator.Generate(board, SideColor.Red);

        Assert.NotEmpty(first);
        Assert.Equal(first, second);
        Assert.Equal(before, BoardFingerprint.Canonical(board));
        Assert.Equal(SideColor.Red, board.Turn);
    }

    [Fact]
    public void BoardFingerprint_UsesExactPositionAndSideToMove()
    {
        var red = BoardState.CreateInitialBoard(SideColor.Red);
        var black = red with { Turn = SideColor.Black };

        Assert.False(BoardFingerprint.EqualsExact(red, black));
        Assert.NotEqual(BoardFingerprint.Hash(red), BoardFingerprint.Hash(black));
    }

    [Fact]
    public void MoveClassifier_GivesCheckPrecedenceOverCapture()
    {
        var before = BoardState.CreateInitialBoard();
        var moving = before.GetPieceAt(new Position(0, 6))!;
        var victim = before.GetPieceAt(new Position(0, 3))!;
        var after = before.ApplyMove(new Position(0, 6), new Position(0, 5));
        var application = new MoveApplicationResult(before, after, moving, victim,
            moving.Position, new Position(0, 5), "before", "after", true);

        var facts = new MoveClassifier().Classify(application);

        Assert.Equal(MoveClassification.CHECK, facts.Classification);
        Assert.True(facts.IsCapture);
    }

    [Fact]
    public void RepetitionResolver_WarnsThenForfeitsUnilateralPerpetualCheck()
    {
        var board = BoardState.CreateInitialBoard();
        var history = new[]
        {
            new PositionFact(1, board, SideColor.Red, MoveClassification.CHECK),
            new PositionFact(2, board, SideColor.Red, MoveClassification.CHECK),
            new PositionFact(3, board, SideColor.Red, MoveClassification.CHECK)
        };
        var resolver = new RepetitionResolver();

        var warning = resolver.Evaluate(history);
        var terminal = resolver.Evaluate(history, SideColor.Red);

        Assert.True(warning.ShouldWarn);
        Assert.Equal(SideColor.Red, warning.MustVarySide);
        Assert.True(terminal.IsTerminal);
        Assert.Equal(SideColor.Black, terminal.Winner);
        Assert.Equal("REPETITION_VIOLATION", terminal.EndReason);
    }
}
