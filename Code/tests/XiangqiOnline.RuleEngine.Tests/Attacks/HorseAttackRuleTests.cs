using XiangqiOnline.RuleEngine.Attacks;
using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.RuleEngine.Tests.Fixtures;
using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;

namespace XiangqiOnline.RuleEngine.Tests.Attacks;

public class HorseAttackRuleTests
{
    private readonly HorseAttackRule _rule = new();

    [Theory]
    [InlineData(5, 6)]
    [InlineData(3, 2)]
    [InlineData(6, 5)]
    [InlineData(2, 3)]
    public void CanAttack_ShouldReturnTrue_ForRepresentativeOrientations(int x, int y) =>
        Assert.True(Attack(new Position(x, y)));

    [Fact]
    public void CanAttack_ShouldReturnFalse_WhenVerticalLegIsBlocked() =>
        Assert.False(Attack(new Position(5, 6), Piece("BLOCKER", 4, 5)));

    [Fact]
    public void CanAttack_ShouldReturnFalse_WhenHorizontalLegIsBlocked() =>
        Assert.False(Attack(new Position(6, 5), Piece("BLOCKER", 5, 4)));

    [Theory]
    [InlineData(4, 5)]
    [InlineData(6, 6)]
    public void CanAttack_ShouldReturnFalse_ForWrongGeometry(int x, int y) =>
        Assert.False(Attack(new Position(x, y)));

    [Fact]
    public void CanAttack_ShouldReturnFalse_ForSameSquare() =>
        Assert.False(Attack(new Position(4, 4)));

    [Fact]
    public void CanAttack_ShouldReturnTrue_ForValidEdgeBoardTarget()
    {
        var attacker = new PieceState("HORSE", PieceType.Horse, SideColor.Red, new Position(1, 2));
        var board = BoardSetupFixture.CreateBoardWithPieces(attacker);

        Assert.True(_rule.CanAttack(board, attacker, new Position(0, 0)));
    }

    [Fact]
    public void CanAttack_ShouldReturnFalse_WhenTargetContainsAlly() =>
        Assert.False(Attack(new Position(5, 6), new PieceState(
            "ALLY", PieceType.Pawn, SideColor.Red, new Position(5, 6))));

    private bool Attack(Position target, params PieceState[] others)
    {
        var attacker = new PieceState("HORSE", PieceType.Horse, SideColor.Red, new Position(4, 4));
        var board = BoardSetupFixture.CreateBoardWithPieces([attacker, .. others]);
        return _rule.CanAttack(board, attacker, target);
    }

    private static PieceState Piece(string id, int x, int y) =>
        new(id, PieceType.Pawn, SideColor.Black, new Position(x, y));
}
