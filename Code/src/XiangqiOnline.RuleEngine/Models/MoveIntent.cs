using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;

namespace XiangqiOnline.RuleEngine.Models;

/// <summary>
/// DTO thể hiện ý định thực hiện một nước đi từ người chơi.
/// </summary>
public record MoveIntent(
    SideColor Side,
    Position From,
    Position To
);
