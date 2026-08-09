using XiangqiOnline.Shared.Models;

namespace XiangqiOnline.Persistence.Models;

/// <summary>
/// Bản ghi nước đi đã commit lưu trong DB (moves table).
/// </summary>
public sealed record MoveRecord(
    string MoveId,
    string MatchId,
    string ClientMoveId,
    string PieceId,
    Position From,
    Position To,
    string? CapturedPieceId,
    string BoardHashBefore,
    string BoardHashAfter,
    int MoveNumber,
    string Result,
    DateTime? CreatedAtUtc = null);
