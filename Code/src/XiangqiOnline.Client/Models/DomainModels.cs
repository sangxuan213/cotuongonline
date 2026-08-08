namespace UDM18.Client.Models;

public readonly record struct Coordinate(int X, int Y)
{
    public bool IsInsideBoard => X is >= 0 and <= 8 && Y is >= 0 and <= 9;
    public override string ToString() => $"({X},{Y})";
}

public enum Side { RED, BLACK }
public enum PieceType { GENERAL, ADVISOR, ELEPHANT, HORSE, CHARIOT, CANNON, PAWN }
public enum PlayerStatus { AVAILABLE, INVITING, INVITED, IN_GAME, OFFLINE }
public enum BoardOrientation { RedAtBottom, BlackAtBottom }

public sealed record PieceState(
    string PieceId,
    Side Side,
    PieceType Type,
    Coordinate Position,
    bool Captured = false);

public sealed record PlayerSummary(string PlayerId, string DisplayName, PlayerStatus Status)
{
    public string Initial => string.IsNullOrWhiteSpace(DisplayName) ? "?" : DisplayName[..1].ToUpperInvariant();
}
public sealed record ChallengeSummary(string ChallengeId, string FromPlayerId, string FromDisplayName);
public sealed record MoveDelta(string PieceId, Coordinate From, Coordinate To, string? CapturedPieceId);

public sealed record GameSnapshot(
    string RoomId,
    long Revision,
    Side CurrentTurn,
    IReadOnlyList<PieceState> Pieces,
    string Status = "PLAYING");
