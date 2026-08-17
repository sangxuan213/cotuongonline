using XiangqiOnline.Shared.Models;

namespace XiangqiOnline.Persistence.Models;

/// <summary>
/// Bản ghi nước đi đã commit lưu trong DB (moves table) tuân thủ UDM18_Database_Schema_v1.1.sql.
/// </summary>
public sealed record MoveRecord(
    string MoveId,
    string ClientMoveId,
    string MatchId,
    int MoveIndex,
    long Revision,
    string Side,
    string PieceId,
    string PieceType,
    Position From,
    Position To,
    string? CapturedPieceId,
    string MoveClass,
    string ClassificationFactsJson,
    int IsCapture,
    int IsCheck,
    int IsCheckmate,
    int RedRemainingMs,
    int BlackRemainingMs,
    string BoardHashBefore,
    string BoardHashAfter,
    DateTime? CreatedAtUtc = null)
{
    public int MoveNumber => MoveIndex;
    public string Result => "COMMITTED";
}
