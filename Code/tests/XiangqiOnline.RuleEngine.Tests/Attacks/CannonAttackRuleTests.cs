using XiangqiOnline.RuleEngine.Attacks;
using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.RuleEngine.Tests.Fixtures;
using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;

namespace XiangqiOnline.RuleEngine.Tests.Attacks;

public class CannonAttackRuleTests
{
    private readonly CannonAttackRule _rule = new();

    [Fact]
    public void CanAttack_ShouldReturnTrue_ForCanonicalVerticalTargetWithExactlyOneScreen()
    {
        var attacker = Cannon(1, 2);
        var screen = Piece("SCREEN", SideColor.Red, 1, 4);
        var target = new Position(1, 6);
        var board = BoardSetupFixture.CreateBoardWithPieces(attacker, screen, Piece("TARGET", SideColor.Red, 1, 6));

        Assert.True(_rule.CanAttack(board, attacker, target));
    }

    [Fact]
    public void CanAttack_ShouldReturnTrue_ForHorizontalTargetWithExactlyOneScreen() =>
        Assert.True(Attack(new Position(8, 2), Piece("TARGET", SideColor.Red, 8, 2), Piece("SCREEN", SideColor.Black, 4, 2)));

    [Fact]
    public void CanAttack_ShouldReturnFalse_WithNoScreen() =>
        Assert.False(Attack(new Position(1, 6)));

    [Fact]
    public void CanAttack_ShouldReturnFalse_WithTwoScreens() =>
        Assert.False(Attack(new Position(1, 6),
            Piece("SCREEN_1", SideColor.Red, 1, 3),
            Piece("SCREEN_2", SideColor.Black, 1, 4)));

    [Fact]
    public void CanAttack_ShouldReturnFalse_WithThreeScreens() =>
        Assert.False(Attack(new Position(1, 7),
            Piece("SCREEN_1", SideColor.Red, 1, 3),
            Piece("SCREEN_2", SideColor.Black, 1, 4),
            Piece("SCREEN_3", SideColor.Red, 1, 5)));

    [Fact]
    public void CanAttack_ShouldReturnFalse_ForDiagonalTarget() =>
        Assert.False(Attack(new Position(4, 5), Piece("SCREEN", SideColor.Black, 2, 3)));

    [Fact]
    public void CanAttack_ShouldReturnFalse_ForSameSquare() =>
        Assert.False(Attack(new Position(1, 2)));

    [Fact]
    public void CanAttack_ShouldReturnFalse_WhenTargetContainsAlly() =>
        Assert.False(Attack(new Position(1, 6),
            Piece("SCREEN", SideColor.Black, 1, 4),
            Piece("ALLY", SideColor.Black, 1, 6)));

    [Theory]
    [InlineData(SideColor.Red)]
    [InlineData(SideColor.Black)]
    public void CanAttack_ShouldAllowScreenFromEitherSide(SideColor screenSide) =>
        Assert.True(Attack(new Position(1, 6), Piece("TARGET", SideColor.Red, 1, 6), Piece("SCREEN", screenSide, 1, 4)));

    private bool Attack(Position target, params PieceState[] others)
    {
        var attacker = Cannon(1, 2);
        var board = BoardSetupFixture.CreateBoardWithPieces([attacker, .. others]);
        return _rule.CanAttack(board, attacker, target);
    }

    private static PieceState Cannon(int x, int y) =>
        new("CANNON", PieceType.Cannon, SideColor.Black, new Position(x, y));

    private static PieceState Piece(string id, SideColor side, int x, int y) =>
        new(id, PieceType.Pawn, side, new Position(x, y));
}
