using XiangqiOnline.Shared.Models;

namespace XiangqiOnline.RuleEngine.LegalMoves;

public sealed record LegalMove(string PieceId, Position From, Position To);
