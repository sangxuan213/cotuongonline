using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;

namespace XiangqiOnline.RuleEngine.Attacks;

public sealed class AttackDetector
{
    private readonly IReadOnlyDictionary<PieceType, IAttackRule> _rulesByPieceType;

    public AttackDetector(IEnumerable<IAttackRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var rulesByPieceType = new Dictionary<PieceType, IAttackRule>();
        foreach (var rule in rules)
        {
            ArgumentNullException.ThrowIfNull(rule);

            if (!rulesByPieceType.TryAdd(rule.MatchingPieceType, rule))
            {
                throw new ArgumentException(
                    $"More than one attack rule is registered for piece type '{rule.MatchingPieceType}'.",
                    nameof(rules));
            }
        }

        _rulesByPieceType = rulesByPieceType;
    }

    public bool IsSquareAttacked(
        BoardState board,
        Position target,
        SideColor attackingSide)
    {
        return FindAttackers(board, target, attackingSide).Count > 0;
    }

    public IReadOnlyList<PieceState> FindAttackers(
        BoardState board,
        Position target,
        SideColor attackingSide)
    {
        ArgumentNullException.ThrowIfNull(board);
        ValidateTarget(target);

        var attackers = new List<PieceState>();
        foreach (var attacker in board.GetActivePieces(attackingSide))
        {
            if (!_rulesByPieceType.TryGetValue(attacker.Type, out var rule))
            {
                throw new InvalidOperationException(
                    $"No attack rule is registered for active piece '{attacker.Id}' of type '{attacker.Type}'.");
            }

            if (rule.CanAttack(board, attacker, target))
            {
                attackers.Add(attacker);
            }
        }

        return attackers
            .OrderBy(attacker => attacker.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private static void ValidateTarget(Position target)
    {
        if (target.X is < 0 or > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(target), target, "Target X must be between 0 and 8.");
        }

        if (target.Y is < 0 or > 9)
        {
            throw new ArgumentOutOfRangeException(nameof(target), target, "Target Y must be between 0 and 9.");
        }
    }
}
