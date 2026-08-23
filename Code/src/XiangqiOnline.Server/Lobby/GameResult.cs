using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;

namespace XiangqiOnline.Server.Lobby;

public sealed record GameResult(
    string ResultType,
    string EndReason,
    SideColor? WinnerSide,
    DateTimeOffset EndedAtUtc,
    long FinalRevision,
    string Explanation);

public sealed record RoomMoveRecord(
    long Revision,
    string ClientMoveId,
    SideColor Side,
    string PieceId,
    Position From,
    Position To,
    string? CapturedPieceId,
    string Classification,
    bool IsCheck,
    ClockSnapshot Clocks,
    DateTimeOffset CommittedAtUtc);
