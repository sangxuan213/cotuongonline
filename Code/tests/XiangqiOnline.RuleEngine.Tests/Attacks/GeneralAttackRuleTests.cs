using XiangqiOnline.RuleEngine.Attacks;
using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.RuleEngine.Tests.Fixtures;
using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;

namespace XiangqiOnline.RuleEngine.Tests.Attacks;

public class GeneralAttackRuleTests
{
    private readonly GeneralAttackRule _rule = new(new GeneralsFacingDetector());

    [Fact]
    public void Constructor_ShouldRejectNullGeneralsFacingDetector() =>
        Assert.Throws<ArgumentNullException>(() => new GeneralAttackRule(null!));

    [Fact]
    public void NormalAttack_ShouldReturnTrue_ForHorizontalOneStepInPalace() =>
        Assert.True(NormalAttack(new Position(5, 1)));

    [Fact]
    public void NormalAttack_ShouldReturnTrue_ForVerticalOneStepInPalace() =>
        Assert.True(NormalAttack(new Position(4, 2)));

    [Fact]
    public void NormalAttack_ShouldReturnFalse_ForDiagonalTarget() =>
        Assert.False(NormalAttack(new Position(5, 2)));

    [Fact]
    public void NormalAttack_ShouldReturnFalse_OutsidePalace() =>
        Assert.False(NormalAttack(new Position(4, 3), source: new Position(4, 2)));

    [Fact]
    public void NormalAttack_ShouldReturnFalse_WhenTargetContainsAlly()
    {
        var attacker = General(SideColor.Black, 4, 1);
        var ally = new PieceState("ALLY", PieceType.Advisor, SideColor.Black, new Position(5, 1));
        var board = BoardSetupFixture.CreateBoardWithPieces(attacker, ally);
        Assert.False(_rule.CanAttack(board, attacker, ally.Position));
    }

    [Fact]
    public void BlackGeneral_ShouldAttackRedGeneral_WhenFacingWithoutBlocker()
    {
        var (board, black, red) = FacingBoard();
        Assert.True(_rule.CanAttack(board, black, red.Position));
    }

    [Fact]
    public void RedGeneral_ShouldAttackBlackGeneral_WhenFacingWithoutBlocker()
    {
        var (board, black, red) = FacingBoard();
        Assert.True(_rule.CanAttack(board, red, black.Position));
    }

    [Fact]
    public void FlyingGeneral_ShouldReturnFalse_WhenPieceBlocksFile()
    {
        var blocker = new PieceState("BLOCKER", PieceType.Pawn, SideColor.Red, new Position(4, 5));
        var (board, black, red) = FacingBoard(blocker);
        Assert.False(_rule.CanAttack(board, black, red.Position));
    }

    [Fact]
    public void FlyingGeneral_ShouldReturnFalse_WhenTargetIsNotEnemyGeneral()
    {
        var (board, black, _) = FacingBoard(new PieceState("ENEMY", PieceType.Pawn, SideColor.Red, new Position(4, 5)));
        Assert.False(_rule.CanAttack(board, black, new Position(4, 5)));
    }

    [Fact]
    public void FlyingGeneral_ShouldReturnFalse_WhenTargetSquareIsEmpty()
    {
        var (board, black, _) = FacingBoard();
        Assert.False(_rule.CanAttack(board, black, new Position(4, 8)));
    }

    [Fact]
    public void FlyingGeneral_ShouldReturnFalse_WhenGeneralsDifferentFiles()
    {
        var black = General(SideColor.Black, 3, 0);
        var red = General(SideColor.Red, 4, 9);
        var board = BoardSetupFixture.CreateBoardWithPieces(black, red);
        Assert.False(_rule.CanAttack(board, black, red.Position));
    }

    private bool NormalAttack(Position target, Position? source = null)
    {
        var attacker = General(SideColor.Black, (source ?? new Position(4, 1)).X, (source ?? new Position(4, 1)).Y);
        return _rule.CanAttack(BoardSetupFixture.CreateBoardWithPieces(attacker), attacker, target);
    }

    private static (BoardState Board, PieceState Black, PieceState Red) FacingBoard(params PieceState[] others)
    {
        var black = General(SideColor.Black, 4, 0);
        var red = General(SideColor.Red, 4, 9);
        return (BoardSetupFixture.CreateBoardWithPieces([black, red, .. others]), black, red);
    }

    private static PieceState General(SideColor side, int x, int y) =>
        new($"{side}_GENERAL", PieceType.General, side, new Position(x, y));
}
