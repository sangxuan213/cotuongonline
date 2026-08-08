using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;

namespace XiangqiOnline.RuleEngine.Attacks;

public sealed class GeneralsFacingDetector
{
    public bool AreGeneralsFacing(BoardState board)
    {
        ArgumentNullException.ThrowIfNull(board);

        var redGeneral = GetGeneral(board, SideColor.Red);
        var blackGeneral = GetGeneral(board, SideColor.Black);
        if (redGeneral is null || blackGeneral is null)
        {
            return false;
        }

        if (redGeneral.Position.X != blackGeneral.Position.X)
        {
            return false;
        }

        var x = redGeneral.Position.X;
        var startY = Math.Min(redGeneral.Position.Y, blackGeneral.Position.Y) + 1;
        var endY = Math.Max(redGeneral.Position.Y, blackGeneral.Position.Y);

        for (var y = startY; y < endY; y++)
        {
            if (board.GetPieceAt(new Position(x, y)) is not null)
            {
                return false;
            }
        }

        return true;
    }

    private static PieceState? GetGeneral(BoardState board, SideColor side)
    {
        var generals = board.GetActivePieces(side)
            .Where(piece => piece.Type == PieceType.General)
            .Take(2)
            .ToArray();

        if (generals.Length > 1)
        {
            throw new InvalidOperationException($"Board contains more than one active {side} General.");
        }

        return generals.SingleOrDefault();
    }
}
