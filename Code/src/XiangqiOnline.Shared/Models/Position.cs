namespace XiangqiOnline.Shared.Models;

/// <summary>
/// Tọa độ chuẩn (Canonical Coordinate) trên bàn cờ Tướng 9x10 (90 giao điểm).
/// X: Cột (0..8).
/// Y: Hàng (0..9, BLACK ở trên: y=0, RED ở dưới: y=9).
/// Hướng tăng Y: từ BLACK (0) xuống RED (9).
/// Hướng tiến: BLACK = +y, RED = -y.
/// </summary>
public readonly record struct Position(int X, int Y)
{
    public bool IsValid()
    {
        return X >= 0 && X <= 8 && Y >= 0 && Y <= 9;
    }

    /// <summary>
    /// Kiểm tra vị trí có nằm trong Cung (Palace) hay không.
    /// Cung Đen (BLACK): X in [3..5], Y in [0..2]
    /// Cung Đỏ (RED): X in [3..5], Y in [7..9]
    /// </summary>
    public bool IsInPalace(Enums.SideColor side)
    {
        if (X < 3 || X > 5) return false;
        return side == Enums.SideColor.Black
            ? (Y >= 0 && Y <= 2)
            : (Y >= 7 && Y <= 9);
    }

    /// <summary>
    /// Kiểm tra xem quân cờ đã qua sông hay chưa.
    /// Phe Đen (BLACK) qua sông khi Y >= 5 (chưa qua sông: Y <= 4).
    /// Phe Đỏ (RED) qua sông khi Y <= 4 (chưa qua sông: Y >= 5).
    /// </summary>
    public bool HasCrossedRiver(Enums.SideColor side)
    {
        return side == Enums.SideColor.Black ? (Y >= 5) : (Y <= 4);
    }

    public override string ToString() => $"({X},{Y})";
}
