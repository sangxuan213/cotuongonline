using XiangqiOnline.RuleEngine.Attacks;
using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.RuleEngine.Tests.Fixtures;
using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;

namespace XiangqiOnline.RuleEngine.Tests.Attacks;

public class PawnAttackRuleTests
{
    private readonly PawnAttackRule _rule = new();

    [Fact]
    public void BlackBeforeRiver_ShouldAttackForward() =>
        Assert.True(Attack(SideColor.Black, new Position(4, 4), new Position(4, 5)));

    [Fact]
    public void BlackBeforeRiver_ShouldNotAttackSideways() =>
        Assert.False(Attack(SideColor.Black, new Position(4, 4), new Position(5, 4)));

    [Fact]
    public void BlackAfterRiver_ShouldAttackForward() =>
        Assert.True(Attack(SideColor.Black, new Position(4, 5), new Position(4, 6)));

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    public void BlackAfterRiver_ShouldAttackSideways(int targetX) =>
        Assert.True(Attack(SideColor.Black, new Position(4, 5), new Position(targetX, 5)));

    [Fact]
    public void Black_ShouldNotAttackBackward() =>
        Assert.False(Attack(SideColor.Black, new Position(4, 5), new Position(4, 4)));

    [Fact]
    public void RedBeforeRiver_ShouldAttackForward() =>
        Assert.True(Attack(SideColor.Red, new Position(4, 5), new Position(4, 4)));

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    public void RedAfterRiver_ShouldAttackSideways(int targetX) =>
        Assert.True(Attack(SideColor.Red, new Position(4, 4), new Position(targetX, 4)));

    [Fact]
    public void Red_ShouldNotAttackBackward() =>
        Assert.False(Attack(SideColor.Red, new Position(4, 4), new Position(4, 5)));

    [Fact]
    public void Pawn_ShouldNotAttackDiagonally() =>
        Assert.False(Attack(SideColor.Red, new Position(4, 4), new Position(5, 3)));

    [Fact]
    public void Pawn_ShouldNotAttackAllyOccupiedTarget()
    {
        var attacker = Pawn(SideColor.Red, new Position(4, 4));
        var ally = new PieceState("ALLY", PieceType.Horse, SideColor.Red, new Position(4, 3));
        var board = BoardSetupFixture.CreateBoardWithPieces(attacker, ally);

        Assert.False(_rule.CanAttack(board, attacker, ally.Position));
    }

    private bool Attack(SideColor side, Position source, Position target)
    {
        var attacker = Pawn(side, source);
        var board = BoardSetupFixture.CreateBoardWithPieces(attacker);
        return _rule.CanAttack(board, attacker, target);
    }

    private static PieceState Pawn(SideColor side, Position source) =>
        new("PAWN", PieceType.Pawn, side, source);
}
