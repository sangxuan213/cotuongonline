using XiangqiOnline.RuleEngine.Attacks;
using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.RuleEngine.Tests.Fixtures;
using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;

namespace XiangqiOnline.RuleEngine.Tests.Attacks;

public class AdvisorAttackRuleTests
{
    private readonly AdvisorAttackRule _rule = new();

    [Fact]
    public void CanAttack_ShouldReturnTrue_ForLegalDiagonalInBlackPalace() =>
        Assert.True(Attack(SideColor.Black, new Position(4, 1), new Position(5, 2)));

    [Fact]
    public void CanAttack_ShouldReturnTrue_ForLegalDiagonalInRedPalace() =>
        Assert.True(Attack(SideColor.Red, new Position(4, 8), new Position(3, 7)));

    [Fact]
    public void CanAttack_ShouldReturnFalse_ForHorizontalTarget() =>
        Assert.False(Attack(SideColor.Black, new Position(4, 1), new Position(5, 1)));

    [Fact]
    public void CanAttack_ShouldReturnFalse_ForVerticalTarget() =>
        Assert.False(Attack(SideColor.Red, new Position(4, 8), new Position(4, 7)));

    [Fact]
    public void CanAttack_ShouldReturnFalse_OutsidePalace() =>
        Assert.False(Attack(SideColor.Black, new Position(3, 2), new Position(2, 3)));

    [Fact]
    public void CanAttack_ShouldReturnFalse_WhenTargetContainsAlly()
    {
        var attacker = Advisor(SideColor.Red, new Position(4, 8));
        var ally = new PieceState("ALLY", PieceType.Pawn, SideColor.Red, new Position(3, 7));
        var board = BoardSetupFixture.CreateBoardWithPieces(attacker, ally);
        Assert.False(_rule.CanAttack(board, attacker, ally.Position));
    }

    private bool Attack(SideColor side, Position source, Position target)
    {
        var attacker = Advisor(side, source);
        return _rule.CanAttack(BoardSetupFixture.CreateBoardWithPieces(attacker), attacker, target);
    }

    private static PieceState Advisor(SideColor side, Position source) =>
        new("ADVISOR", PieceType.Advisor, side, source);
}
