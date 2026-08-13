using XiangqiOnline.RuleEngine.Attacks;
using XiangqiOnline.RuleEngine.Checks;
using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.RuleEngine.Tests.Fixtures;
using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;

namespace XiangqiOnline.RuleEngine.Tests.Checks;

internal static class CheckTestFactory
{
    public static AttackDetector CreateCompleteAttackDetector()
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

    public static CheckDetector CreateCheckDetector() => new(CreateCompleteAttackDetector());

    public static SelfCheckValidator CreateSelfCheckValidator()
    {
        var facingDetector = new GeneralsFacingDetector();
        var attackDetector = new AttackDetector(new IAttackRule[]
        {
            new GeneralAttackRule(facingDetector),
            new AdvisorAttackRule(),
            new ElephantAttackRule(),
            new HorseAttackRule(),
            new ChariotAttackRule(),
            new CannonAttackRule(),
            new PawnAttackRule()
        });
        return new SelfCheckValidator(new CheckDetector(attackDetector), facingDetector);
    }

    public static BoardState Board(params PieceState[] pieces) =>
        BoardSetupFixture.CreateBoardWithPieces(pieces);

    public static BoardState Board(SideColor turn, params PieceState[] pieces) =>
        BoardSetupFixture.CreateBoardWithPieces(turn, pieces);

    public static PieceState Piece(
        string id,
        PieceType type,
        SideColor side,
        int x,
        int y,
        bool alive = true) => new(id, type, side, new Position(x, y), alive);

    public static PieceState General(SideColor side, int x, int y, string? id = null) =>
        Piece(id ?? $"{side}_GENERAL", PieceType.General, side, x, y);
}
