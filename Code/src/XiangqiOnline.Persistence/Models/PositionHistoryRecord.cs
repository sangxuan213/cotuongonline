namespace XiangqiOnline.Persistence.Models;

/// <summary>
/// Bản ghi lịch sử thế cờ (position_history table) tuân thủ UDM18_Database_Schema_v1.1.sql.
/// </summary>
public sealed record PositionHistoryRecord(
    string MatchId,
    long Revision,
    string BoardHash,
    string CanonicalPieceMapJson,
    string SideToMove,
    string? MoveClass = null,
    string ClassificationFactsJson = "{}",
    string? CycleSignature = null,
    string? MustVarySide = null,
    string? AdjudicationReason = null,
    DateTime? CreatedAtUtc = null);
