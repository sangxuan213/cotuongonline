using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;

namespace XiangqiOnline.RuleEngine.Attacks;

public interface IAttackRule
{
    PieceType MatchingPieceType { get; }

    bool CanAttack(
        BoardState board,
        PieceState attacker,
        Position target);
}
