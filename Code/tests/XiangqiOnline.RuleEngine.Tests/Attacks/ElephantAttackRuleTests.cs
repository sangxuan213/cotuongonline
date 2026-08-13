using XiangqiOnline.RuleEngine.Attacks;
using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.RuleEngine.Tests.Fixtures;
using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;

namespace XiangqiOnline.RuleEngine.Tests.Attacks;

public class ElephantAttackRuleTests
{
    private readonly ElephantAttackRule _rule = new();

    [Fact]
    public void CanAttack_ShouldReturnTrue_ForValidBlackDiagonal() =>
        Assert.True(Attack(SideColor.Black, new Position(2, 0), new Position(4, 2)));

    [Fact]
    public void CanAttack_ShouldReturnTrue_ForValidRedDiagonal() =>
        Assert.True(Attack(SideColor.Red, new Position(2, 9), new Position(4, 7)));

    [Fact]
    public void CanAttack_ShouldReturnFalse_WhenElephantEyeIsBlocked() =>
        Assert.False(Attack(SideColor.Black, new Position(2, 0), new Position(4, 2),
            new PieceState("BLOCKER", PieceType.Pawn, SideColor.Red, new Position(3, 1))));

    [Fact]
    public void CanAttack_ShouldReturnFalse_WhenBlackCrossesRiver() =>
        Assert.False(Attack(SideColor.Black, new Position(2, 4), new Position(4, 6)));

    [Fact]
    public void CanAttack_ShouldReturnFalse_WhenRedCrossesRiver() =>
        Assert.False(Attack(SideColor.Red, new Position(2, 5), new Position(4, 3)));

    [Theory]
    [InlineData(3, 1)]
    [InlineData(4, 1)]
    public void CanAttack_ShouldReturnFalse_ForWrongGeometry(int targetX, int targetY) =>
        Assert.False(Attack(SideColor.Black, new Position(2, 0), new Position(targetX, targetY)));

    [Fact]
    public void CanAttack_ShouldReturnFalse_WhenTargetContainsAlly()
    {
        var ally = new PieceState("ALLY", PieceType.Pawn, SideColor.Black, new Position(4, 2));
        Assert.False(Attack(SideColor.Black, new Position(2, 0), ally.Position, ally));
    }

    [Fact]
    public void CanAttack_ShouldReturnTrue_ForBoundaryRepresentative() =>
        Assert.True(Attack(SideColor.Black, new Position(0, 2), new Position(2, 4)));

    private bool Attack(SideColor side, Position source, Position target, params PieceState[] others)
    {
        var attacker = new PieceState("ELEPHANT", PieceType.Elephant, side, source);
        var board = BoardSetupFixture.CreateBoardWithPieces([attacker, .. others]);
        return _rule.CanAttack(board, attacker, target);
    }
}
