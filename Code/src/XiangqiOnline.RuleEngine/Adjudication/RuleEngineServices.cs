using XiangqiOnline.RuleEngine.Attacks;
using XiangqiOnline.RuleEngine.Checks;

namespace XiangqiOnline.RuleEngine.Adjudication;

public static class RuleEngineServices
{
    public static AttackDetector CreateAttackDetector()
    {
        var facing = new GeneralsFacingDetector();
        var attacks = new AttackDetector(new IAttackRule[]
        {
            new GeneralAttackRule(facing),
            new AdvisorAttackRule(),
            new ElephantAttackRule(),
            new HorseAttackRule(),
            new ChariotAttackRule(),
            new CannonAttackRule(),
            new PawnAttackRule()
        });
        return attacks;
    }

    public static CheckDetector CreateCheckDetector() => new(CreateAttackDetector());
}
