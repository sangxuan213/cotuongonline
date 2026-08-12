using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.RuleEngine.Validators;
using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;

namespace XiangqiOnline.RuleEngine.Attacks;

public sealed class ElephantAttackRule : IAttackRule
{
    private readonly ElephantValidator _validator = new();

    public PieceType MatchingPieceType => PieceType.Elephant;

    public bool CanAttack(BoardState board, PieceState attacker, Position target) =>
        _validator.Validate(board, attacker, target).IsValid;
}
