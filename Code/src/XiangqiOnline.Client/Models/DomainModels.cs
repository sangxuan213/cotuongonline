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

public sealed record MoveDelta(
    string PieceId,
    Position From,
    Position To,
    string? CapturedPieceId,
    SideColor? CurrentTurn);

public sealed record GameSnapshot(
    string RoomId,
    long Revision,
    SideColor CurrentTurn,
    IReadOnlyList<PieceState> Pieces,
    string Status = "PLAYING");
