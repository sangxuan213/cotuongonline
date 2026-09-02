using XiangqiOnline.RuleEngine.Adjudication;
using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.Shared.Enums;

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
    private readonly SemaphoreSlim _mutationGate = new(1, 1);
    private readonly List<RoomMoveRecord> _moves = new();
    private readonly List<PositionFact> _positionHistory = new();
    private readonly HashSet<string> _spectatorConnectionIds = new(StringComparer.Ordinal);
    private readonly object _stateGate = new();
    private bool _gameEndedBroadcasted;
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
        Clock = new ServerClock(TimeProfileSpec.Parse(timeProfile), Board.Turn);
        var previousSide = Board.Turn == SideColor.Red ? SideColor.Black : SideColor.Red;
        _positionHistory.Add(new PositionFact(0, Board, previousSide, MoveClassification.IDLE));
    }

    public string RoomId { get; }
    public string RedPlayerId { get; }
    public string BlackPlayerId { get; }
    public string RuleProfileId { get; }
    public string TimeProfile { get; }
    public DateTimeOffset CreatedAtUtc { get; }
    public BoardState Board { get; private set; }
    public GameRoomStatus Status { get; private set; } = GameRoomStatus.CREATED;
    public SideColor CurrentTurn => Board.Turn;
    public long Revision { get; private set; }
    public ServerClock Clock { get; }
    public GameResult? Result { get; private set; }
    public SideColor? MustVarySide { get; private set; }
    public string? RepetitionCycleSignature { get; private set; }
    public string? PendingDrawOfferPlayerId { get; private set; }
    public DateTimeOffset? PendingDrawOfferExpiresAtUtc { get; private set; }
    public IReadOnlyList<RoomMoveRecord> Moves { get { lock (_stateGate) return _moves.ToArray(); } }
    public IReadOnlyList<PositionFact> PositionHistory { get { lock (_stateGate) return _positionHistory.ToArray(); } }
    public IReadOnlyList<string> SpectatorConnectionIds { get { lock (_stateGate) return _spectatorConnectionIds.ToArray(); } }

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
    }

    public long CommitRevision(BoardState nextBoard)
    {
        if (Status != GameRoomStatus.PLAYING)
            throw new InvalidOperationException("Only playing rooms can commit revisions.");
        if (nextBoard is null)
            throw new ArgumentNullException(nameof(nextBoard));

        Board = nextBoard;
        Revision++;
        return Revision;
    }

    public long CommitMove(
        BoardState nextBoard,
        string clientMoveId,
        string pieceId,
        XiangqiOnline.Shared.Models.Position from,
        XiangqiOnline.Shared.Models.Position to,
        string? capturedPieceId,
        MoveClassificationFacts classification,
        ClockSnapshot clocks,
        DateTimeOffset committedAtUtc)
    {
        var movedSide = Board.Turn;
        var revision = CommitRevision(nextBoard);
        lock (_stateGate)
        {
            _moves.Add(new RoomMoveRecord(
                revision, clientMoveId, movedSide, pieceId, from, to, capturedPieceId,
                classification.Classification.ToString(), classification.IsCheck, clocks, committedAtUtc));
            _positionHistory.Add(new PositionFact(revision, nextBoard, movedSide, classification.Classification));
        }
        return revision;
    }

    public async Task<T> ExecuteSerializedAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken = default)
    {
        await _mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await action().ConfigureAwait(false); }
        finally { _mutationGate.Release(); }
    }

    public bool TryFinish(GameResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        lock (_stateGate)
        {
            if (IsTerminal) return false;
            Result = result;
            Status = GameRoomStatus.FINISHED;
            Clock.Stop();
            PendingDrawOfferPlayerId = null;
            PendingDrawOfferExpiresAtUtc = null;
            return true;
        }
    }

    public bool TryMarkGameEndedBroadcasted()
    {
        lock (_stateGate)
        {
            if (!IsTerminal || _gameEndedBroadcasted) return false;
            _gameEndedBroadcasted = true;
            return true;
        }
    }

    public bool TryOfferDraw(string playerId, DateTimeOffset nowUtc, TimeSpan lifetime)
    {
        if (lifetime <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(lifetime));
        lock (_stateGate)
        {
            if (PendingDrawOfferExpiresAtUtc is { } deadline && nowUtc >= deadline)
            {
                PendingDrawOfferPlayerId = null;
                PendingDrawOfferExpiresAtUtc = null;
            }
            if (IsTerminal || !HasPlayer(playerId) || PendingDrawOfferPlayerId is not null) return false;
            PendingDrawOfferPlayerId = playerId;
            PendingDrawOfferExpiresAtUtc = nowUtc.Add(lifetime);
            return true;
        }
    }

    public bool TryRespondToDraw(string playerId, bool accept, DateTimeOffset nowUtc)
    {
        lock (_stateGate)
        {
            if (PendingDrawOfferExpiresAtUtc is { } deadline && nowUtc >= deadline)
            {
                PendingDrawOfferPlayerId = null;
                PendingDrawOfferExpiresAtUtc = null;
                return false;
            }
            if (PendingDrawOfferPlayerId is null || PendingDrawOfferPlayerId == playerId || !HasPlayer(playerId)) return false;
            PendingDrawOfferPlayerId = null;
            PendingDrawOfferExpiresAtUtc = null;
            return true;
        }
    }

    public void SetRepetitionWarning(SideColor? mustVarySide, string? cycleSignature)
    {
        lock (_stateGate)
        {
            MustVarySide = mustVarySide;
            RepetitionCycleSignature = cycleSignature;
        }
    }

    public bool AddSpectator(string connectionId)
    {
        lock (_stateGate) return _spectatorConnectionIds.Add(connectionId);
    }

    public bool RemoveSpectator(string connectionId)
    {
        lock (_stateGate) return _spectatorConnectionIds.Remove(connectionId);
    }

    public void Finish()
    {
        if (IsTerminal)
            throw new InvalidOperationException("Terminal rooms cannot transition again.");

        Status = GameRoomStatus.FINISHED;
        Clock.Stop();
    }

    public void AbortSystem()
    {
        if (IsTerminal)
            throw new InvalidOperationException("Terminal rooms cannot transition again.");

        Status = GameRoomStatus.ABORTED_SYSTEM;
        Clock.Stop();
    }

    public bool IsTerminal => Status is GameRoomStatus.FINISHED or GameRoomStatus.ABORTED_SYSTEM;
}
