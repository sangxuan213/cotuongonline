using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;

namespace UDM18.Client.Models;

public enum BoardOrientation { RedAtBottom, BlackAtBottom }
public enum LobbyPlayerStatus { AVAILABLE, INVITING, INVITED, IN_GAME, OFFLINE }

public sealed record PieceState(
    string PieceId,
    SideColor Side,
    PieceType Type,
    Position Position,
    bool Captured = false);

public sealed record PlayerSummary(string PlayerId, string DisplayName, LobbyPlayerStatus Status)
{
    public string Initial => string.IsNullOrWhiteSpace(DisplayName)
        ? "?"
        : System.Globalization.StringInfo.GetNextTextElement(DisplayName.Trim(), 0).ToUpperInvariant();
}

public sealed record ChallengeSummary(string ChallengeId, string FromPlayerId, string FromDisplayName);
public sealed record BotDifficultyChoice(string Name, string WireValue, string Description);
public sealed record WaitingRoomSummary(
    string RoomId,
    string OwnerPlayerId,
    string OwnerDisplayName,
    string TimeProfile,
    DateTimeOffset CreatedAtUtc,
    bool IsLocked = false)
{
    public string DisplayTime => CreatedAtUtc.ToLocalTime().ToString("HH:mm");
    public string ShortRoomId => RoomId.Length <= 8 ? RoomId.ToUpperInvariant() : RoomId[..8].ToUpperInvariant();
    public string OwnerInitial => string.IsNullOrWhiteSpace(OwnerDisplayName)
        ? "?"
        : System.Globalization.StringInfo.GetNextTextElement(OwnerDisplayName.Trim(), 0).ToUpperInvariant();
    public string AccessLabel => IsLocked ? "🔒 CÓ MẬT KHẨU" : "🔓 CÔNG KHAI";
}

public sealed record MoveDelta(
    string PieceId,
    Position From,
    Position To,
    string? CapturedPieceId,
    SideColor? CurrentTurn,
    bool IsCheck = false,
    bool IsCheckmate = false);

public sealed record GameSnapshot(
    string RoomId,
    long Revision,
    SideColor CurrentTurn,
    IReadOnlyList<PieceState> Pieces,
    string Status = "PLAYING",
    string ViewerRole = "PLAYER",
    ClockSnapshotModel? Clocks = null,
    SideColor? MustVarySide = null,
    int SpectatorCount = 0);

public sealed record ClockSnapshotModel(
    long RedRemainingMs,
    long BlackRemainingMs,
    SideColor ActiveSide,
    long IncrementMs,
    DateTimeOffset ServerAnchorUtc,
    bool IsExpired);

public sealed record ActiveMatchSummary(
    string RoomId,
    string RedPlayerId,
    string BlackPlayerId,
    SideColor CurrentTurn,
    string TimeProfile,
    int SpectatorCount,
    long Revision,
    string RedDisplayName = "Bên Đỏ",
    string BlackDisplayName = "Bên Đen")
{
    public string ShortRoomId => RoomId.Length <= 8 ? RoomId.ToUpperInvariant() : RoomId[..8].ToUpperInvariant();
    public string PlayersLabel => $"🔴 {RedDisplayName}  vs  ⚫ {BlackDisplayName}";
}

public sealed record GameResultSummary(
    string ResultType,
    string EndReason,
    SideColor? WinnerSide,
    string Explanation);

public sealed record MatchHistorySummary(
    string MatchId,
    string RoomId,
    string Status,
    string ResultType,
    string EndReason,
    int TotalMoves,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? EndedAtUtc,
    string? WinnerSide,
    string TimeProfile,
    string ViewerSide = "SPECTATOR",
    string RedDisplayName = "Bên Đỏ",
    string BlackDisplayName = "Bên Đen")
{
    public bool ViewerWon => (ViewerSide.Equals("RED", StringComparison.OrdinalIgnoreCase) && ResultType.Equals("RED_WIN", StringComparison.OrdinalIgnoreCase)) ||
                             (ViewerSide.Equals("BLACK", StringComparison.OrdinalIgnoreCase) && ResultType.Equals("BLACK_WIN", StringComparison.OrdinalIgnoreCase));
    public bool ViewerLost => !ResultType.Equals("DRAW", StringComparison.OrdinalIgnoreCase) && !ViewerWon;
    public string ResultLabel => ResultType.ToUpperInvariant() switch
    {
        "RED_WIN" or "BLACK_WIN" when ViewerWon => "Bạn thắng",
        "RED_WIN" or "BLACK_WIN" when ViewerLost => "Bạn thua",
        "DRAW" => "Ván hòa",
        _ => "Đã kết thúc"
    };
    public string ReasonLabel => EndReason.ToUpperInvariant() switch
    {
        "CHECKMATE" => "Chiếu bí",
        "RESIGNATION" => "Đầu hàng",
        "TIMEOUT" => "Hết giờ",
        "DRAW_AGREEMENT" => "Hai bên đồng ý hòa",
        "STALEMATE" => "Hết nước đi",
        "REPETITION" => "Lặp thế cờ",
        _ => string.IsNullOrWhiteSpace(EndReason) ? "Kết thúc ván" : EndReason
    };
    public string Title => $"{ResultLabel}  ·  {TotalMoves} nước";
    public string Subtitle => $"{StartedAtUtc.ToLocalTime():dd/MM/yyyy HH:mm}  ·  {ReasonLabel}";
    public string RoomLabel => $"Bàn {ShortRoomId}  ·  {TimeProfile}";
    public string OpponentName => ViewerSide.Equals("RED", StringComparison.OrdinalIgnoreCase) ? BlackDisplayName : RedDisplayName;
    public string OpponentLabel => $"Đối thủ: {OpponentName}  ·  Bạn cầm quân {(ViewerSide.Equals("RED", StringComparison.OrdinalIgnoreCase) ? "Đỏ" : "Đen")}";
    public string ShortRoomId => RoomId.Length <= 8 ? RoomId.ToUpperInvariant() : RoomId[..8].ToUpperInvariant();
    public string ResultColor => ResultType.Equals("DRAW", StringComparison.OrdinalIgnoreCase) ? "#9A6414"
        : ResultType.Equals("RED_WIN", StringComparison.OrdinalIgnoreCase) ? "#A61B1B" : "#075E75";
}

public sealed record ReplayFrame(
    long Revision,
    SideColor CurrentTurn,
    IReadOnlyList<PieceState> Pieces,
    Position? LastFrom,
    Position? LastTo,
    string Description);

public sealed record ReplaySession(
    string MatchId,
    string RoomId,
    SideColor? ViewerSide,
    string ResultLabel,
    IReadOnlyList<ReplayFrame> Frames);

public sealed record QuickChatMessage(
    string MessageId,
    string RoomId,
    string SenderPlayerId,
    string SenderDisplayName,
    string Code,
    string Text,
    bool IsSpectator,
    DateTimeOffset SentAtUtc)
{
    public string SenderLabel => IsSpectator ? $"👁 {SenderDisplayName}" : SenderDisplayName;
    public string TimeText => SentAtUtc.ToLocalTime().ToString("HH:mm:ss");
    public string IconPath => Code switch
    {
        "HELLO" => "/Assets/Emoji/waving-hand.png",
        "GOOD_MOVE" => "/Assets/Emoji/clapping-hands.png",
        "THANKS" => "/Assets/Emoji/folded-hands.png",
        "THINKING" => "/Assets/Emoji/thinking-face.png",
        "GOOD_LUCK" => "/Assets/Emoji/four-leaf-clover.png",
        "GOOD_GAME" => "/Assets/Emoji/trophy.png",
        "SMILE" => "/Assets/Emoji/smiling-face.png",
        "SURPRISED" => "/Assets/Emoji/surprised-face.png",
        _ => "/Assets/Emoji/smiling-face.png"
    };
    public string IconGlyph => Code switch
    {
        "HELLO" => "👋",
        "GOOD_MOVE" => "👏",
        "THANKS" => "🙏",
        "THINKING" => "🤔",
        "GOOD_LUCK" => "🍀",
        "GOOD_GAME" => "🏆",
        "SMILE" => "😄",
        "SURPRISED" => "😲",
        "CHALLENGE" => "🔥",
        "CHECK" => "⚔️",
        "PRESSURE" => "😎",
        "COMEBACK" => "🐉",
        "TEXT" => "💬",
        _ => "💬"
    };
}
