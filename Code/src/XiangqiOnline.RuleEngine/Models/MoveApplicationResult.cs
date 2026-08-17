using XiangqiOnline.Shared.Models;

namespace XiangqiOnline.RuleEngine.Models;

public sealed record MoveApplicationResult(
    BoardState Before,
    BoardState After,
    PieceState MovingPiece,
    PieceState? CapturedPiece,
    Position From,
    Position To,
    string BoardHashBefore,
    string BoardHashAfter,
    bool IsCheck);
