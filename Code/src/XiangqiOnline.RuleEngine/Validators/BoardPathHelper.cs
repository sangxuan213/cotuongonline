using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.Shared.Models;

namespace XiangqiOnline.RuleEngine.Validators;

/// <summary>
/// Helper dùng chung cho các validator cần đếm quân cản trên đường đi theo hàng ngang / cột dọc.
/// </summary>
internal static class BoardPathHelper
{
    /// <summary>
    /// Đếm số quân cờ nằm giữa hai vị trí `from` và `to` dọc theo cùng hàng ngang hoặc cột dọc.
    /// Trả về -1 nếu hai vị trí không thẳng hàng hoặc trùng nhau.
    /// </summary>
    public static int CountPiecesBetween(BoardState board, Position from, Position to)
    {
        int dx = to.X - from.X;
        int dy = to.Y - from.Y;

        if ((dx != 0 && dy != 0) || (dx == 0 && dy == 0))
        {
            return -1;
        }

        int stepX = Math.Sign(dx);
        int stepY = Math.Sign(dy);
        int count = 0;

        int currX = from.X + stepX;
        int currY = from.Y + stepY;

        while (currX != to.X || currY != to.Y)
        {
            if (board.GetPieceAt(new Position(currX, currY)) != null)
            {
                count++;
            }

            currX += stepX;
            currY += stepY;
        }

        return count;
    }
}