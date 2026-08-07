namespace XiangqiOnline.Shared.Models;

/// <summary>
/// Tọa độ chuẩn (Canonical Coordinate) trên bàn cờ Tướng 9x10 (90 giao điểm).
/// X: Cột (0..8, từ trái sang phải theo góc nhìn mặc định).
/// Y: Hàng (0..9, từ dưới lên trên, Red: 0..4, Black: 5..9).
/// </summary>
public readonly record struct Position(int X, int Y)
{
    public bool IsValid()
    {
        return X >= 0 && X <= 8 && Y >= 0 && Y <= 9;
    }

    /// <summary>
    /// Kiểm tra vị trí có nằm trong Cung (Palace) hay không.
    /// Cung Đỏ: X in [3..5], Y in [0..2]
    /// Cung Đen: X in [3..5], Y in [7..9]
    /// </summary>
    public bool IsInPalace(Enums.SideColor side)
    {
        if (X < 3 || X > 5) return false;
        return side == Enums.SideColor.Red
            ? (Y >= 0 && Y <= 2)
            : (Y >= 7 && Y <= 9);
    }

    /// <summary>
    /// Kiểm tra xem quân cờ đã qua sông hay chưa.
    /// Phe Đỏ qua sông khi Y >= 5.
    /// Phe Đen qua sông khi Y <= 4.
    /// </summary>
    public bool HasCrossedRiver(Enums.SideColor side)
    {
        return side == Enums.SideColor.Red ? (Y >= 5) : (Y <= 4);
    }

    public override string ToString() => $"({X},{Y})";
}
