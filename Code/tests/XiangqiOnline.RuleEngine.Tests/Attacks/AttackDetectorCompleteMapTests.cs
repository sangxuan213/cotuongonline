using XiangqiOnline.RuleEngine.Attacks;
using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.RuleEngine.Tests.Fixtures;
using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;

namespace XiangqiOnline.RuleEngine.Tests.Attacks;

public class AttackDetectorCompleteMapTests
{
    private readonly AttackDetector _detector = CreateDetector();

    [Fact]
    public void AttackDetector_WithAllSevenRules_ShouldEvaluateInitialBoardWithoutMissingRuleException()
    {
        var board = BoardState.CreateInitialBoard();
        var exception = Record.Exception(() =>
            _detector.IsSquareAttacked(board, new Position(4, 4), SideColor.Black));
        Assert.Null(exception);
    }

    [Fact]
    public void AttackDetector_ShouldDetectFlyingGeneralAttack()
    {
        var black = General(SideColor.Black, 4, 0);
        var red = General(SideColor.Red, 4, 9);
        var board = BoardSetupFixture.CreateBoardWithPieces(black, red);
        Assert.True(_detector.IsSquareAttacked(board, red.Position, SideColor.Black));
    }

    [Fact]
    public void AttackDetector_ShouldNotDetectFlyingGeneralWhenBlocked()
    {
        var black = General(SideColor.Black, 4, 0);
        var red = General(SideColor.Red, 4, 9);
        var blocker = new PieceState("BLOCKER", PieceType.Pawn, SideColor.Red, new Position(4, 5));
        var board = BoardSetupFixture.CreateBoardWithPieces(black, red, blocker);
        Assert.False(_detector.IsSquareAttacked(board, red.Position, SideColor.Black));
    }

    [Fact]
    public void AttackDetector_ShouldDetectCannonAttackAgainstEnemyGeneral()
    {
        var cannon = new PieceState("BLACK_CANNON", PieceType.Cannon, SideColor.Black, new Position(1, 2));
        var screen = new PieceState("SCREEN", PieceType.Pawn, SideColor.Black, new Position(1, 4));
        var redGeneral = General(SideColor.Red, 1, 6);
        var board = BoardSetupFixture.CreateBoardWithPieces(cannon, screen, redGeneral);
        Assert.True(_detector.IsSquareAttacked(board, redGeneral.Position, SideColor.Black));
    }

    private static AttackDetector CreateDetector()
    {
        var facingDetector = new GeneralsFacingDetector();
        return new AttackDetector(new IAttackRule[]
        {
            new GeneralAttackRule(facingDetector),
            new AdvisorAttackRule(),
            new ElephantAttackRule(),
            new HorseAttackRule(),
            new ChariotAttackRule(),
            new CannonAttackRule(),
            new PawnAttackRule()
        });
    }

    private static PieceState General(SideColor side, int x, int y) =>
        new($"{side}_GENERAL", PieceType.General, side, new Position(x, y));
}
