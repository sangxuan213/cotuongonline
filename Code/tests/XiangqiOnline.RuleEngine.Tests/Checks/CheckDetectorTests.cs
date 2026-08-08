using XiangqiOnline.RuleEngine.Attacks;
using XiangqiOnline.RuleEngine.Checks;
using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;

namespace XiangqiOnline.RuleEngine.Tests.Checks;

public class CheckDetectorTests
{
    private readonly CheckDetector _detector = CheckTestFactory.CreateCheckDetector();

    [Fact]
    public void Evaluate_ShouldReturnNotInCheck_WhenGeneralHasNoAttackers()
    {
        var board = CheckTestFactory.Board(
            CheckTestFactory.General(SideColor.Red, 4, 9),
            CheckTestFactory.General(SideColor.Black, 3, 0));
        var status = _detector.Evaluate(board, SideColor.Red);
        Assert.False(status.IsInCheck);
        Assert.Empty(status.CheckingPieces);
        Assert.Equal(new Position(4, 9), status.GeneralPosition);
        Assert.Equal(SideColor.Red, status.CheckedSide);
    }

    [Fact]
    public void Evaluate_ShouldDetectChariotCheck()
    {
        var chariot = CheckTestFactory.Piece("BLACK_CHARIOT", PieceType.Chariot, SideColor.Black, 4, 0);
        AssertSingleChecker(chariot, CheckTestFactory.General(SideColor.Red, 4, 9));
    }

    [Fact]
    public void Evaluate_ShouldDetectCannonCheck()
    {
        var cannon = CheckTestFactory.Piece("BLACK_CANNON", PieceType.Cannon, SideColor.Black, 1, 2);
        var screen = CheckTestFactory.Piece("SCREEN", PieceType.Pawn, SideColor.Red, 1, 4);
        var general = CheckTestFactory.General(SideColor.Red, 1, 6);
        var status = _detector.Evaluate(CheckTestFactory.Board(cannon, screen, general), SideColor.Red);
        Assert.True(status.IsInCheck);
        Assert.Equal(cannon, Assert.Single(status.CheckingPieces));
    }

    [Fact]
    public void Evaluate_ShouldDetectHorseCheck()
    {
        var horse = CheckTestFactory.Piece("BLACK_HORSE", PieceType.Horse, SideColor.Black, 3, 7);
        AssertSingleChecker(horse, CheckTestFactory.General(SideColor.Red, 4, 9));
    }

    [Fact]
    public void Evaluate_ShouldNotDetectHorse_WhenLegBlocked()
    {
        var general = CheckTestFactory.General(SideColor.Red, 4, 9);
        var horse = CheckTestFactory.Piece("BLACK_HORSE", PieceType.Horse, SideColor.Black, 3, 7);
        var blocker = CheckTestFactory.Piece("BLOCKER", PieceType.Pawn, SideColor.Red, 3, 8);
        var status = _detector.Evaluate(CheckTestFactory.Board(general, horse, blocker), SideColor.Red);
        Assert.False(status.IsInCheck);
    }

    [Fact]
    public void Evaluate_ShouldDetectPawnCheck()
    {
        var pawn = CheckTestFactory.Piece("BLACK_PAWN", PieceType.Pawn, SideColor.Black, 4, 8);
        AssertSingleChecker(pawn, CheckTestFactory.General(SideColor.Red, 4, 9));
    }

    [Fact]
    public void Evaluate_ShouldDetectFlyingGeneralCheck()
    {
        var black = CheckTestFactory.General(SideColor.Black, 4, 0);
        var red = CheckTestFactory.General(SideColor.Red, 4, 9);
        var status = _detector.Evaluate(CheckTestFactory.Board(black, red), SideColor.Red);
        Assert.Equal(black, Assert.Single(status.CheckingPieces));
    }

    [Fact]
    public void Evaluate_ShouldNotDetectFlyingGeneral_WhenBlocked()
    {
        var board = CheckTestFactory.Board(
            CheckTestFactory.General(SideColor.Black, 4, 0),
            CheckTestFactory.Piece("BLOCKER", PieceType.Pawn, SideColor.Red, 4, 5),
            CheckTestFactory.General(SideColor.Red, 4, 9));
        Assert.False(_detector.Evaluate(board, SideColor.Red).IsInCheck);
    }

    [Fact]
    public void Evaluate_ShouldReturnAllAttackers_ForDoubleCheck()
    {
        var general = CheckTestFactory.General(SideColor.Red, 4, 9);
        var chariot = CheckTestFactory.Piece("BLACK_CHARIOT", PieceType.Chariot, SideColor.Black, 4, 0);
        var horse = CheckTestFactory.Piece("BLACK_HORSE", PieceType.Horse, SideColor.Black, 3, 7);
        var status = _detector.Evaluate(CheckTestFactory.Board(general, chariot, horse), SideColor.Red);
        Assert.Equal(2, status.CheckingPieces.Count);
    }

    [Fact]
    public void Evaluate_ShouldReturnCheckingPiecesInDeterministicIdOrder()
    {
        var general = CheckTestFactory.General(SideColor.Red, 4, 9);
        var chariot = CheckTestFactory.Piece("Z_CHARIOT", PieceType.Chariot, SideColor.Black, 4, 0);
        var horse = CheckTestFactory.Piece("A_HORSE", PieceType.Horse, SideColor.Black, 3, 7);
        var status = _detector.Evaluate(CheckTestFactory.Board(general, chariot, horse), SideColor.Red);
        Assert.Equal(new[] { "A_HORSE", "Z_CHARIOT" }, status.CheckingPieces.Select(piece => piece.Id));
    }

    [Fact]
    public void Evaluate_ShouldNotDependOnBoardTurn()
    {
        var pieces = new[]
        {
            CheckTestFactory.General(SideColor.Red, 4, 9),
            CheckTestFactory.Piece("BLACK_CHARIOT", PieceType.Chariot, SideColor.Black, 4, 0)
        };
        var redTurn = _detector.Evaluate(CheckTestFactory.Board(SideColor.Red, pieces), SideColor.Red);
        var blackTurn = _detector.Evaluate(CheckTestFactory.Board(SideColor.Black, pieces), SideColor.Red);
        Assert.Equal(redTurn.CheckedSide, blackTurn.CheckedSide);
        Assert.Equal(redTurn.GeneralPosition, blackTurn.GeneralPosition);
        Assert.Equal(
            redTurn.CheckingPieces.Select(piece => piece.Id),
            blackTurn.CheckingPieces.Select(piece => piece.Id));
    }

    [Fact]
    public void Evaluate_ShouldThrow_WhenCheckedGeneralMissing()
    {
        var board = CheckTestFactory.Board(CheckTestFactory.General(SideColor.Black, 4, 0));
        Assert.Throws<InvalidOperationException>(() => _detector.Evaluate(board, SideColor.Red));
    }

    [Fact]
    public void Evaluate_ShouldThrow_WhenCheckedSideHasDuplicateGenerals()
    {
        var board = CheckTestFactory.Board(
            CheckTestFactory.General(SideColor.Red, 4, 9, "RED_GENERAL_1"),
            CheckTestFactory.General(SideColor.Red, 3, 9, "RED_GENERAL_2"));
        Assert.Throws<InvalidOperationException>(() => _detector.Evaluate(board, SideColor.Red));
    }

    [Fact]
    public void Constructor_ShouldRejectNullAttackDetector() =>
        Assert.Throws<ArgumentNullException>(() => new CheckDetector(null!));

    [Fact]
    public void Evaluate_ShouldRejectNullBoard() =>
        Assert.Throws<ArgumentNullException>(() => _detector.Evaluate(null!, SideColor.Red));

    private void AssertSingleChecker(PieceState checker, PieceState general)
    {
        var status = _detector.Evaluate(CheckTestFactory.Board(checker, general), general.Side);
        Assert.True(status.IsInCheck);
        Assert.Equal(checker, Assert.Single(status.CheckingPieces));
    }
}
