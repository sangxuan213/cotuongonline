using XiangqiOnline.Shared.Enums;
using XiangqiOnline.RuleEngine.Models;

namespace XiangqiOnline.Server.Lobby;

public enum GameRoomStatus
{
    CREATED,
    WAITING_FOR_READY,
    PLAYING,
    FINISHED,
    ABORTED_SYSTEM
}

public sealed class GameRoom
{
    public GameRoom(
        string roomId,
        string redPlayerId,
        string blackPlayerId,
        string ruleProfileId,
        string timeProfile,
        DateTimeOffset createdAtUtc,
        BoardState? initialBoard = null)
    {
        if (string.IsNullOrWhiteSpace(roomId))
            throw new ArgumentException("Room id is required.", nameof(roomId));
        if (string.IsNullOrWhiteSpace(redPlayerId))
            throw new ArgumentException("Red player id is required.", nameof(redPlayerId));
        if (string.IsNullOrWhiteSpace(blackPlayerId))
            throw new ArgumentException("Black player id is required.", nameof(blackPlayerId));
        if (redPlayerId == blackPlayerId)
            throw new ArgumentException("A room requires two different players.", nameof(blackPlayerId));
        if (string.IsNullOrWhiteSpace(ruleProfileId))
            throw new ArgumentException("Rule profile id is required.", nameof(ruleProfileId));
        if (string.IsNullOrWhiteSpace(timeProfile))
            throw new ArgumentException("Time profile is required.", nameof(timeProfile));

        RoomId = roomId;
        RedPlayerId = redPlayerId;
        BlackPlayerId = blackPlayerId;
        RuleProfileId = ruleProfileId;
        TimeProfile = timeProfile;
        CreatedAtUtc = createdAtUtc;
        Board = initialBoard ?? BoardState.CreateInitialBoard(SideColor.Red);
    }

    public string RoomId { get; }
    public string RedPlayerId { get; }
    public string BlackPlayerId { get; }
    public string RuleProfileId { get; }
    public string TimeProfile { get; }
    public DateTimeOffset CreatedAtUtc { get; }
    public BoardState Board { get; }
    public GameRoomStatus Status { get; private set; } = GameRoomStatus.CREATED;
    public SideColor CurrentTurn { get; private set; } = SideColor.Red;
    public long Revision { get; private set; }

    public bool HasPlayer(string playerId) =>
        RedPlayerId == playerId || BlackPlayerId == playerId;

    public SideColor GetSideForPlayer(string playerId)
    {
        if (RedPlayerId == playerId)
            return SideColor.Red;
        if (BlackPlayerId == playerId)
            return SideColor.Black;

        throw new InvalidOperationException("Player is not a member of this room.");
    }

    public void MarkWaitingForReady()
    {
        if (Status != GameRoomStatus.CREATED)
            throw new InvalidOperationException("Only created rooms can wait for ready.");

        Status = GameRoomStatus.WAITING_FOR_READY;
    }

    public void Start()
    {
        if (Status is not (GameRoomStatus.CREATED or GameRoomStatus.WAITING_FOR_READY))
            throw new InvalidOperationException("Only non-terminal rooms can start.");

        Status = GameRoomStatus.PLAYING;
        CurrentTurn = SideColor.Red;
    }

    public long CommitRevision()
    {
        if (Status != GameRoomStatus.PLAYING)
            throw new InvalidOperationException("Only playing rooms can commit revisions.");

        Revision++;
        CurrentTurn = CurrentTurn == SideColor.Red ? SideColor.Black : SideColor.Red;
        return Revision;
    }

    public void Finish()
    {
        if (IsTerminal)
            throw new InvalidOperationException("Terminal rooms cannot transition again.");

        Status = GameRoomStatus.FINISHED;
    }

    public void AbortSystem()
    {
        if (IsTerminal)
            throw new InvalidOperationException("Terminal rooms cannot transition again.");

        Status = GameRoomStatus.ABORTED_SYSTEM;
    }

    public bool IsTerminal => Status is GameRoomStatus.FINISHED or GameRoomStatus.ABORTED_SYSTEM;
}
