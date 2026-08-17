using XiangqiOnline.Shared.Enums;

namespace XiangqiOnline.Persistence.Models;

/// <summary>
/// Bản ghi trận đấu lưu trong DB (matches table) tuân thủ UDM18_Database_Schema_v1.1.sql.
/// </summary>
public sealed record MatchRecord(
    string MatchId,
    string RoomId,
    string RedPlayerId,
    string BlackPlayerId,
    string RuleProfileId = "UDM18_WXF_PRO_2018",
    string RuleProfileVersion = "1.1",
    string TimeProfile = "STANDARD",
    string ConfigJson = "{}",
    string Status = "PLAYING",
    DateTime? StartedAtUtc = null,
    DateTime? EndedAtUtc = null,
    string? ResultType = null,
    string? EndReason = null,
    string? WinnerSide = null,
    long? FinalRevision = null,
    int TotalMoves = 0)
{
    public SideColor CurrentTurn => (TotalMoves % 2 == 0) ? SideColor.Red : SideColor.Black;
    public long Revision => FinalRevision ?? TotalMoves;
}
