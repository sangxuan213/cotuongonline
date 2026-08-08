using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.RuleEngine.Validators;
using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;

namespace XiangqiOnline.RuleEngine.Attacks;

public sealed class AdvisorAttackRule : IAttackRule
{
    private readonly AdvisorValidator _validator = new();

    public PieceType MatchingPieceType => PieceType.Advisor;

    public bool CanAttack(BoardState board, PieceState attacker, Position target) =>
        _validator.Validate(board, attacker, target).IsValid;
}
