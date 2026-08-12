using XiangqiOnline.RuleEngine.Attacks;
using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.RuleEngine.Tests.Fixtures;
using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;

namespace XiangqiOnline.RuleEngine.Tests.Attacks;

public class ChariotAttackRuleTests
{
    private readonly ChariotAttackRule _rule = new();

    [Fact]
    public void CanAttack_ShouldReturnTrue_ForClearHorizontalTarget() =>
        Assert.True(Attack(new Position(7, 4)));

    [Fact]
    public void CanAttack_ShouldReturnTrue_ForClearVerticalTarget() =>
        Assert.True(Attack(new Position(4, 8)));

    [Fact]
    public void CanAttack_ShouldReturnFalse_ForDiagonalTarget() =>
        Assert.False(Attack(new Position(6, 6)));

    [Fact]
    public void CanAttack_ShouldReturnFalse_WhenHorizontalPathIsBlocked() =>
        Assert.False(Attack(new Position(8, 4), Piece("BLOCKER", SideColor.Black, 6, 4)));

    [Fact]
    public void CanAttack_ShouldReturnFalse_WhenVerticalPathIsBlocked() =>
        Assert.False(Attack(new Position(4, 0), Piece("BLOCKER", SideColor.Red, 4, 2)));

    [Fact]
    public void CanAttack_ShouldReturnFalse_ForSameSquare() =>
        Assert.False(Attack(new Position(4, 4)));

    [Fact]
    public void CanAttack_ShouldReturnFalse_WhenTargetContainsAlly() =>
        Assert.False(Attack(new Position(4, 8), Piece("ALLY", SideColor.Red, 4, 8)));

    private bool Attack(Position target, params PieceState[] others)
    {
        var attacker = new PieceState("CHARIOT", PieceType.Chariot, SideColor.Red, new Position(4, 4));
        var board = BoardSetupFixture.CreateBoardWithPieces([attacker, .. others]);
        return _rule.CanAttack(board, attacker, target);
    }

    private static PieceState Piece(string id, SideColor side, int x, int y) =>
        new(id, PieceType.Pawn, side, new Position(x, y));
}
