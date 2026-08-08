using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;

namespace XiangqiOnline.RuleEngine.Checks;

public sealed record CheckStatus(
    SideColor CheckedSide,
    Position GeneralPosition,
    IReadOnlyList<PieceState> CheckingPieces)
{
    public bool IsInCheck => CheckingPieces.Count > 0;
}
