using XiangqiOnline.Shared.Enums;

namespace XiangqiOnline.Persistence.Models;

/// <summary>
/// Bản ghi trận đấu lưu trong DB (matches table).
/// </summary>
public sealed record MatchRecord(
    string MatchId,
    string Status,
    SideColor CurrentTurn,
    long Revision,
    string BoardHash,
    string? WhitePlayerId = null,
    string? BlackPlayerId = null,
    DateTime? CreatedAtUtc = null,
    DateTime? UpdatedAtUtc = null);
