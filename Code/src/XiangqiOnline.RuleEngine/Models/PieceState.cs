using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;

namespace XiangqiOnline.RuleEngine.Models;

/// <summary>
/// Trạng thái của 1 quân cờ trên bàn cờ. Immutable record.
/// </summary>
public record PieceState(
    string Id,
    PieceType Type,
    SideColor Side,
    Position Position,
    bool IsAlive = true
)
{
    public PieceState WithPosition(Position newPosition) => this with { Position = newPosition };
    public PieceState Captured() => this with { IsAlive = false };
}
