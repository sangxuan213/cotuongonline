using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;

namespace XiangqiOnline.RuleEngine.Attacks;

public sealed class CannonAttackRule : IAttackRule
{
    public PieceType MatchingPieceType => PieceType.Cannon;

    public bool CanAttack(BoardState board, PieceState attacker, Position target)
    {
        if (!target.IsValid())
        {
            return false;
        }

        var targetPiece = board.GetPieceAt(target);
        if (targetPiece?.Side == attacker.Side)
        {
            return false;
        }

        var dx = target.X - attacker.Position.X;
        var dy = target.Y - attacker.Position.Y;
        if ((dx != 0 && dy != 0) || (dx == 0 && dy == 0))
        {
            return false;
        }

        var stepX = Math.Sign(dx);
        var stepY = Math.Sign(dy);
        var current = new Position(attacker.Position.X + stepX, attacker.Position.Y + stepY);
        var screenCount = 0;

        while (current != target)
        {
            if (board.GetPieceAt(current) is not null)
            {
                screenCount++;
                if (screenCount > 1)
                {
                    return false;
                }
            }

            current = new Position(current.X + stepX, current.Y + stepY);
        }

        return screenCount == 1;
    }
}
