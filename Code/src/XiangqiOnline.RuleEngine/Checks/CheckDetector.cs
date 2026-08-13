using XiangqiOnline.RuleEngine.Attacks;
using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.Shared.Enums;

namespace XiangqiOnline.RuleEngine.Checks;

public sealed class CheckDetector
{
    private readonly AttackDetector _attackDetector;

    public CheckDetector(AttackDetector attackDetector)
    {
        _attackDetector = attackDetector ?? throw new ArgumentNullException(nameof(attackDetector));
    }

    public CheckStatus Evaluate(BoardState board, SideColor side)
    {
        ArgumentNullException.ThrowIfNull(board);

        var generals = board.GetActivePieces(side)
            .Where(piece => piece.Type == PieceType.General)
            .Take(2)
            .ToArray();

        if (generals.Length == 0)
        {
            throw new InvalidOperationException($"Board does not contain an active {side} General.");
        }

        if (generals.Length > 1)
        {
            throw new InvalidOperationException($"Board contains more than one active {side} General.");
        }

        var general = generals[0];
        var opponent = side == SideColor.Red ? SideColor.Black : SideColor.Red;
        var attackers = _attackDetector.FindAttackers(board, general.Position, opponent);

        return new CheckStatus(side, general.Position, attackers);
    }
}
