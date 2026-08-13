using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.RuleEngine.Validators;
using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;

namespace XiangqiOnline.RuleEngine.Attacks;

public sealed class PawnAttackRule : IAttackRule
{
    private readonly PawnValidator _validator = new();

    public PieceType MatchingPieceType => PieceType.Pawn;

    public bool CanAttack(BoardState board, PieceState attacker, Position target) =>
        _validator.Validate(board, attacker, target).IsValid;
}
