using System.Text.Json;
using System.IO;
using UDM18.Client.Models;
using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;
using XiangqiOnline.Shared.Protocol;

namespace UDM18.Client.Protocol;

public sealed class GameClient
{
    private readonly IProtocolTransport _transport;
    private long _clientSequence;
    private string? _sessionToken;
    private TaskCompletionSource<bool>? _helloAck;
    private TaskCompletionSource<bool>? _loginResult;
    private TaskCompletionSource<string>? _accountActionResult;
    private CancellationTokenSource? _heartbeatCts;
    private string? _playerId;

    public GameClient(IProtocolTransport transport)
    {
        _transport = transport;
        _transport.MessageHandler = HandleMessageAsync;
        _transport.StateChanged += (state, error) =>
        {
            if (state != ConnectionState.Connected)
            {
                StopHeartbeat();
                if (state is ConnectionState.Disconnected or ConnectionState.Failed &&
                    _loginResult is { Task.IsCompleted: false })
                    _loginResult.TrySetException(new IOException(error ?? "Kết nối đã đóng trước khi đăng nhập hoàn tất."));
            }
            ConnectionChanged?.Invoke(state, error);
        };
    }

    public event Action<ConnectionState, string?>? ConnectionChanged;
    public event Action<string, string>? LoginCompleted;
    public event Action<IReadOnlyList<PlayerSummary>>? PlayersUpdated;
    public event Action<ChallengeSummary>? ChallengeReceived;
    public event Action<string, string>? ChallengeSent;
    public event Action<string>? ChallengeRejected;
    public event Action<string>? ChallengeCancelled;
    public event Action<string>? RoomCreated;
    public event Action<WaitingRoomSummary>? WaitingRoomCreated;
    public event Action<string?>? WaitingRoomCancelled;
    public event Action<IReadOnlyList<WaitingRoomSummary>>? WaitingRoomsUpdated;
    public event Action<GameSnapshot>? SnapshotReceived;
    public event Action<long, MoveDelta>? MoveCommitted;
    public event Action<string, string, long>? MoveRejected;
    public event Action<string>? ErrorReceived;
    public event Action<IReadOnlyList<ActiveMatchSummary>>? ActiveMatchesUpdated;
    public event Action<ClockSnapshotModel>? ClockSynchronized;
    public event Action<SideColor, string?>? RepetitionWarningReceived;
    public event Action<GameResultSummary>? GameEnded;
    public event Action? Reconnected;
    public event Action<string, DateTimeOffset>? DrawOffered;
    public event Action? DrawDeclined;
    public event Action<string, string, DateTimeOffset>? RematchOffered;
    public event Action? RematchDeclined;
    public event Action? RematchCancelled;
    public event Action<string?>? SpectatorLeft;
    public event Action<IReadOnlyList<MatchHistorySummary>>? HistoryUpdated;
    public event Action<ReplaySession>? ReplayTimelineLoaded;
    public event Action? ReplayLoaded;
    public event Action<QuickChatMessage>? QuickChatReceived;
    public event Action<string>? AccountNotice;

    public ConnectionState State => _transport.State;
    public string? ResumeToken => _sessionToken;
    public string? PlayerId => _playerId;

    public async Task ConnectAndLoginAsync(string host, int port, string displayName, CancellationToken cancellationToken)
    {
        _helloAck = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _loginResult = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await _transport.ConnectAsync(host, port, cancellationToken);
            await SendAsync("HELLO", new { protocolVersion = ProtocolConstants.ProtocolVersion, clientName = "UDM18.WPF" }, false, cancellationToken);
            await _helloAck.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            await SendAsync("LOGIN_REQUEST", new { displayName, resumeToken = (string?)null }, false, cancellationToken);
            await _loginResult.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        }
        catch
        {
            await _transport.DisconnectAsync();
            throw;
        }
    }

    public Task RequestPlayersAsync(CancellationToken cancellationToken = default)
        => SendAsync("PLAYER_LIST_REQUEST", new { }, true, cancellationToken);

    public Task SendChallengeAsync(string targetPlayerId, CancellationToken cancellationToken = default)
        => SendAsync("CHALLENGE_SEND", new { targetPlayerId, timeProfile = "10+0" }, true, cancellationToken);

    public Task CancelChallengeAsync(string challengeId, CancellationToken cancellationToken = default)
        => SendAsync("CHALLENGE_CANCEL", new { challengeId }, true, cancellationToken);

    public Task AcceptChallengeAsync(string challengeId, CancellationToken cancellationToken = default)
        => SendAsync("CHALLENGE_ACCEPT", new { challengeId }, true, cancellationToken);

    public Task RejectChallengeAsync(string challengeId, CancellationToken cancellationToken = default)
        => SendAsync("CHALLENGE_REJECT", new { challengeId, reason = "USER_REJECTED" }, true, cancellationToken);

    public Task StartBotGameAsync(string difficulty, CancellationToken cancellationToken = default)
        => SendAsync("BOT_GAME_REQUEST", new { difficulty }, true, cancellationToken);

    public Task CreateWaitingRoomAsync(CancellationToken cancellationToken = default)
        => SendAsync("WAITING_ROOM_CREATE", new { timeProfile = "10+0" }, true, cancellationToken);

    public Task CreateWaitingRoomAsync(string? password, CancellationToken cancellationToken = default)
        => SendAsync("WAITING_ROOM_CREATE", new { timeProfile = "10+0", password }, true, cancellationToken);

    public Task RequestWaitingRoomsAsync(CancellationToken cancellationToken = default)
        => SendAsync("WAITING_ROOM_LIST", new { }, true, cancellationToken);

    public Task JoinWaitingRoomAsync(string roomId, CancellationToken cancellationToken = default)
        => SendAsync("WAITING_ROOM_JOIN", new { roomId }, true, cancellationToken, roomId);

    public Task JoinWaitingRoomAsync(string roomId, string? password, CancellationToken cancellationToken = default)
        => SendAsync("WAITING_ROOM_JOIN", new { roomId, password }, true, cancellationToken, roomId);

    public Task CancelWaitingRoomAsync(string roomId, CancellationToken cancellationToken = default)
        => SendAsync("WAITING_ROOM_CANCEL", new { roomId }, true, cancellationToken, roomId);

    public Task SendQuickChatAsync(string roomId, string code, CancellationToken cancellationToken = default)
        => SendAsync("QUICK_CHAT_SEND", new { code }, true, cancellationToken, roomId);

    public Task SendChatMessageAsync(string roomId, string text, CancellationToken cancellationToken = default)
        => SendAsync("QUICK_CHAT_SEND", new { text }, true, cancellationToken, roomId);

    public Task SendMoveAsync(string roomId, long expectedRevision, Position from, Position to, CancellationToken cancellationToken = default)
        => SendAsync("MOVE_REQUEST", new { clientMoveId = UlidId.New(), expectedRevision, from, to }, true, cancellationToken, roomId);

    public Task RequestResyncAsync(string roomId, long lastRevision, CancellationToken cancellationToken = default)
        => SendAsync("RESYNC_REQUEST", new { roomId, lastRevision }, true, cancellationToken, roomId);

    public Task DisconnectAsync() => _transport.DisconnectAsync();

    public async Task LogoutAsync()
    {
        StopHeartbeat();
        _sessionToken = null;
        _playerId = null;
        _helloAck = null;
        _loginResult = null;
        _accountActionResult = null;
        await _transport.DisconnectAsync();
    }

    private Task SendAsync(string type, object payload, bool authenticated, CancellationToken cancellationToken, string? roomId = null)
    {
        var envelope = new
        {
            protocolVersion = ProtocolConstants.ProtocolVersion,
            type,
            requestId = UlidId.New(),
            sessionToken = authenticated ? _sessionToken : null,
            roomId,
            clientSequence = Interlocked.Increment(ref _clientSequence),
            sentAtUtc = DateTimeOffset.UtcNow,
            payload
        };
        return _transport.SendAsync(envelope, cancellationToken);
    }

    private async Task HandleMessageAsync(JsonElement root)
    {
        if (!root.TryGetProperty("type", out var typeNode)) return;
        var type = typeNode.GetString();
        var payload = root.TryGetProperty("payload", out var p) ? p : root;
        try
        {
            switch (type)
            {
                case "HELLO_ACK": ParseHelloAck(payload); break;
                case "LOGIN_RESULT": if (ParseLogin(payload)) { await RequestPlayersAsync(); await RequestActiveMatchesAsync(); await RequestWaitingRoomsAsync(); await RequestHistoryAsync(); } break;
                case "ACCOUNT_REGISTER_RESULT":
                    if (!IsAccepted(payload)) FailAccountAction(payload); else AccountNotice?.Invoke(ReadString(payload, "message") ?? "Đăng ký thành công.");
                    break;
                case "PASSWORD_RESET_SENT": CompleteAccountAction(payload); break;
                case "PASSWORD_RESET_RESULT":
                    if (IsAccepted(payload)) CompleteAccountAction(payload); else FailAccountAction(payload);
                    break;
                case "PLAYER_LIST_UPDATED": ParsePlayers(payload); break;
                case "CHALLENGE_RECEIVED": ParseChallenge(payload); break;
                case "CHALLENGE_SENT": ParseChallengeSent(payload); break;
                case "CHALLENGE_REJECTED": ParseChallengeRejected(payload); break;
                case "CHALLENGE_CANCELLED": ChallengeCancelled?.Invoke(ReadString(payload, "challengeId") ?? string.Empty); break;
                case "ROOM_CREATED": ParseRoom(payload); break;
                case "WAITING_ROOM_CREATED": WaitingRoomCreated?.Invoke(ParseWaitingRoom(payload)); break;
                case "WAITING_ROOM_CANCELLED": WaitingRoomCancelled?.Invoke(ReadString(payload, "roomId")); break;
                case "WAITING_ROOMS_UPDATED": ParseWaitingRooms(payload); break;
                case "GAME_STATE_SNAPSHOT": SnapshotReceived?.Invoke(ParseSnapshot(payload)); break;
                case "MOVE_COMMITTED": ParseMoveCommitted(root, payload); break;
                case "MOVE_REJECTED": ParseMoveRejected(root, payload); break;
                case "ACTIVE_MATCHES_UPDATED": ParseActiveMatches(payload); break;
                case "CLOCK_SYNC": ParseClock(payload); break;
                case "REPETITION_WARNING": ParseRepetitionWarning(payload); break;
                case "GAME_ENDED": ParseGameEnded(payload); break;
                case "RECONNECT_ACCEPTED": ParseReconnect(payload); break;
                case "DRAW_OFFERED": ParseDrawOffered(payload); break;
                case "DRAW_DECLINED": DrawDeclined?.Invoke(); break;
                case "REMATCH_OFFERED": ParseRematchOffered(payload); break;
                case "REMATCH_DECLINED": RematchDeclined?.Invoke(); break;
                case "REMATCH_CANCELLED": RematchCancelled?.Invoke(); break;
                case "SPECTATOR_LEFT": SpectatorLeft?.Invoke(ReadString(payload, "roomId")); break;
                case "HISTORY_LIST_RESULT": ParseHistory(payload); break;
                case "HISTORY_DETAIL_RESULT": ParseHistoryDetail(payload); break;
                case "QUICK_CHAT_RECEIVED": ParseQuickChat(payload); break;
                case "ERROR_RESPONSE":
                {
                    var message = ReadString(payload, "message") ?? "Server báo lỗi.";
                    ErrorReceived?.Invoke(message);
                    _loginResult?.TrySetException(new InvalidOperationException(message));
                    _accountActionResult?.TrySetException(new InvalidOperationException(message));
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            ErrorReceived?.Invoke($"Không đọc được event {type}: {ex.Message}");
        }
    }

    private static bool IsAccepted(JsonElement payload) =>
        string.Equals(ReadString(payload, "status"), "ACCEPTED", StringComparison.OrdinalIgnoreCase);

    private void CompleteAccountAction(JsonElement payload)
    {
        var message = ReadString(payload, "message") ?? "Hoàn tất.";
        AccountNotice?.Invoke(message);
        _accountActionResult?.TrySetResult(message);
    }

    private void FailAccountAction(JsonElement payload)
    {
        var message = ReadString(payload, "message") ?? "Không thể hoàn tất yêu cầu.";
        ErrorReceived?.Invoke(message);
        var error = new InvalidOperationException(message);
        _accountActionResult?.TrySetException(error);
        _loginResult?.TrySetException(error);
    }

    public Task ConnectAndAccountLoginAsync(string host, int port, string email, string password, CancellationToken cancellationToken)
        => ConnectAndAuthenticateAsync(host, port, "ACCOUNT_LOGIN_REQUEST", new { email, password }, cancellationToken);

    public Task ConnectAndRegisterAsync(string host, int port, string email, string displayName, string password, CancellationToken cancellationToken)
        => ConnectAndAuthenticateAsync(host, port, "ACCOUNT_REGISTER_REQUEST", new { email, displayName, password }, cancellationToken);

    public async Task<string> RequestPasswordResetAsync(string host, int port, string email, CancellationToken cancellationToken)
    {
        await EnsureAnonymousConnectionAsync(host, port, cancellationToken);
        _accountActionResult = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await SendAsync("PASSWORD_RESET_REQUEST", new { email }, false, cancellationToken);
        return await _accountActionResult.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
    }

    public async Task<string> ConfirmPasswordResetAsync(string host, int port, string email, string code, string newPassword, CancellationToken cancellationToken)
    {
        await EnsureAnonymousConnectionAsync(host, port, cancellationToken);
        _accountActionResult = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await SendAsync("PASSWORD_RESET_CONFIRM", new { email, code, newPassword }, false, cancellationToken);
        return await _accountActionResult.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
    }

    private async Task ConnectAndAuthenticateAsync(string host, int port, string type, object payload, CancellationToken cancellationToken)
    {
        _loginResult = new(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await EnsureAnonymousConnectionAsync(host, port, cancellationToken);
            await SendAsync(type, payload, false, cancellationToken);
            await _loginResult.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
        }
        catch { await _transport.DisconnectAsync(); throw; }
    }

    private async Task EnsureAnonymousConnectionAsync(string host, int port, CancellationToken cancellationToken)
    {
        if (_transport.State == ConnectionState.Connected) return;
        _helloAck = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await _transport.ConnectAsync(host, port, cancellationToken);
        await SendAsync("HELLO", new { protocolVersion = ProtocolConstants.ProtocolVersion, clientName = "UDM18.WPF" }, false, cancellationToken);
        await _helloAck.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
    }

    public Task RequestActiveMatchesAsync(CancellationToken cancellationToken = default)
        => SendAsync("ACTIVE_MATCHES_REQUEST", new { }, true, cancellationToken);

    public Task JoinSpectatorAsync(string roomId, CancellationToken cancellationToken = default)
        => SendAsync("SPECTATOR_JOIN", new { roomId }, true, cancellationToken, roomId);

    public Task LeaveSpectatorAsync(string roomId, CancellationToken cancellationToken = default)
        => SendAsync("SPECTATOR_LEAVE", new { roomId }, true, cancellationToken, roomId);

    public Task ResignAsync(string roomId, CancellationToken cancellationToken = default)
        => SendAsync("RESIGN_REQUEST", new { confirmationId = UlidId.New() }, true, cancellationToken, roomId);

    public Task OfferDrawAsync(string roomId, CancellationToken cancellationToken = default)
        => SendAsync("DRAW_OFFER", new { }, true, cancellationToken, roomId);

    public Task RespondDrawAsync(string roomId, bool accept, CancellationToken cancellationToken = default)
        => SendAsync("DRAW_RESPONSE", new { accept }, true, cancellationToken, roomId);

    public Task RequestRematchAsync(string originalRoomId, CancellationToken cancellationToken = default)
        => SendAsync("REMATCH_REQUEST", new { originalRoomId }, true, cancellationToken, originalRoomId);

    public Task RespondRematchAsync(string originalRoomId, bool accept, CancellationToken cancellationToken = default)
        => SendAsync("REMATCH_RESPONSE", new { originalRoomId, accept }, true, cancellationToken, originalRoomId);

    public Task CancelRematchAsync(string originalRoomId, CancellationToken cancellationToken = default)
        => SendAsync("REMATCH_CANCEL", new { originalRoomId }, true, cancellationToken, originalRoomId);

    public Task RequestHistoryAsync(CancellationToken cancellationToken = default)
        => SendAsync("HISTORY_LIST_REQUEST", new { }, true, cancellationToken);

    public Task RequestHistoryDetailAsync(string matchId, CancellationToken cancellationToken = default)
        => SendAsync("HISTORY_DETAIL_REQUEST", new { matchId }, true, cancellationToken);

    public Task ReconnectAsync(string host, int port, string resumeToken, CancellationToken cancellationToken)
        => ReconnectCoreAsync(host, port, resumeToken, cancellationToken);

    private async Task ReconnectCoreAsync(string host, int port, string resumeToken, CancellationToken cancellationToken)
    {
        _helloAck = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await _transport.ConnectAsync(host, port, cancellationToken);
        await SendAsync("HELLO", new { protocolVersion = ProtocolConstants.ProtocolVersion, clientName = "UDM18.WPF" }, false, cancellationToken);
        await _helloAck.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        _sessionToken = resumeToken;
        await SendAsync("RECONNECT_REQUEST", new { resumeToken }, true, cancellationToken);
    }

    private void ParseHelloAck(JsonElement payload)
    {
        var version = ReadString(payload, "supportedVersion");
        if (version != "1.0")
        {
            _helloAck?.TrySetException(new InvalidOperationException($"Server không hỗ trợ protocol 1.0 (nhận: {version ?? "không rõ"})."));
            return;
        }
        _helloAck?.TrySetResult(true);
    }

    private bool ParseLogin(JsonElement payload)
    {
        var status = ReadString(payload, "status") ?? "UNKNOWN";
        if (!status.Equals("ACCEPTED", StringComparison.OrdinalIgnoreCase) &&
            !status.Equals("SUCCESS", StringComparison.OrdinalIgnoreCase))
        {
            var error = ReadString(payload, "message") ?? $"Đăng nhập thất bại: {status}";
            ErrorReceived?.Invoke(error);
            _loginResult?.TrySetException(new InvalidOperationException(error));
            _ = _transport.DisconnectAsync();
            return false;
        }
        _sessionToken = ReadString(payload, "token") ?? throw new JsonException("LOGIN_RESULT thiếu token.");
        var player = payload.GetProperty("player");
        _playerId = ReadString(player, "playerId") ?? "";
        LoginCompleted?.Invoke(_playerId, ReadString(player, "displayName") ?? "");
        StartHeartbeat();
        _loginResult?.TrySetResult(true);
        return true;
    }

    private void ParsePlayers(JsonElement payload)
    {
        var array = payload.TryGetProperty("players", out var players) ? players : payload;
        var result = new List<PlayerSummary>();
        foreach (var player in array.EnumerateArray())
        {
            Enum.TryParse(ReadString(player, "status"), true, out LobbyPlayerStatus status);
            result.Add(new PlayerSummary(
                ReadString(player, "playerId") ?? "",
                ReadString(player, "displayName") ?? "",
                status));
        }
        PlayersUpdated?.Invoke(result);
    }

    private void ParseChallenge(JsonElement payload)
    {
        var challenge = payload.TryGetProperty("challenge", out var c) ? c : payload;
        ChallengeReceived?.Invoke(new ChallengeSummary(
            ReadString(challenge, "challengeId") ?? "",
            ReadString(challenge, "fromPlayerId") ?? "",
            ReadString(challenge, "fromDisplayName") ?? "Đối thủ"));
    }

    private void ParseChallengeRejected(JsonElement payload)
    {
        var challengeId = ReadString(payload, "challengeId")
            ?? throw new JsonException("CHALLENGE_REJECTED thiếu challengeId.");
        ChallengeRejected?.Invoke(challengeId);
    }

    private void ParseRoom(JsonElement payload)
    {
        var roomId = ReadString(payload, "roomId") ?? throw new JsonException("ROOM_CREATED thiếu roomId.");
        RoomCreated?.Invoke(roomId);
        if (payload.TryGetProperty("snapshot", out var snapshot)) SnapshotReceived?.Invoke(ParseSnapshot(snapshot));
    }

    private static GameSnapshot ParseSnapshot(JsonElement payload)
    {
        var board = payload.TryGetProperty("board", out var b) ? b : payload;
        var piecesNode = board.TryGetProperty("pieces", out var pieces) ? pieces : payload.GetProperty("pieces");
        var parsed = new List<PieceState>();
        foreach (var piece in piecesNode.EnumerateArray())
        {
            var captured = piece.TryGetProperty("captured", out var capturedNode) && capturedNode.GetBoolean();
            if (captured) continue;
            parsed.Add(new PieceState(
                ReadString(piece, "pieceId") ?? "UNKNOWN",
                ReadEnum<SideColor>(piece, "side") ?? throw new JsonException("Quân cờ thiếu side hợp lệ."),
                ReadEnum<PieceType>(piece, "type") ?? throw new JsonException("Quân cờ thiếu type hợp lệ."),
                new Position(piece.GetProperty("x").GetInt32(), piece.GetProperty("y").GetInt32())));
        }
        var clocks = payload.TryGetProperty("clocks", out var clockNode) ? ParseClockModel(clockNode) : null;
        var mustVary = ReadSide(payload, "mustVarySide");
        var spectatorCount = payload.TryGetProperty("spectatorCount", out var countNode) ? countNode.GetInt32() : 0;
        return new GameSnapshot(
            ReadString(payload, "roomId") ?? "",
            payload.GetProperty("revision").GetInt64(),
            Enum.Parse<SideColor>(ReadString(payload, "currentTurn")!, true),
            parsed,
            ReadString(payload, "status") ?? "PLAYING",
            ReadString(payload, "viewerRole") ?? "PLAYER",
            clocks,
            mustVary,
            spectatorCount);
    }

    private void ParseMoveCommitted(JsonElement root, JsonElement payload)
    {
        var from = payload.GetProperty("from");
        var to = payload.GetProperty("to");
        var revision = root.TryGetProperty("revision", out var r) ? r.GetInt64() : payload.GetProperty("revision").GetInt64();
        var currentTurn = ReadSide(payload, "currentTurn")
            ?? (payload.TryGetProperty("stateDelta", out var stateDelta) ? ReadSide(stateDelta, "currentTurn") : null)
            ?? (payload.TryGetProperty("clocks", out var clocks) ? ReadSide(clocks, "activeSide") : null);
        var movedSide = ReadSide(payload, "side");
        currentTurn ??= movedSide switch { SideColor.Red => SideColor.Black, SideColor.Black => SideColor.Red, _ => null };
        MoveCommitted?.Invoke(revision, new MoveDelta(
            ReadString(payload, "pieceId") ?? "",
            new Position(from.GetProperty("x").GetInt32(), from.GetProperty("y").GetInt32()),
            new Position(to.GetProperty("x").GetInt32(), to.GetProperty("y").GetInt32()),
            ReadString(payload, "capturedPieceId"),
            currentTurn,
            payload.TryGetProperty("isCheck", out var check) && check.GetBoolean(),
            payload.TryGetProperty("isCheckmate", out var mate) && mate.GetBoolean()));
        if (payload.TryGetProperty("clocks", out var clockNode))
            ClockSynchronized?.Invoke(ParseClockModel(clockNode));
    }

    private void ParseMoveRejected(JsonElement root, JsonElement payload)
    {
        var revision = payload.TryGetProperty("revision", out var r) ? r.GetInt64()
            : root.TryGetProperty("revision", out r) ? r.GetInt64() : 0;
        MoveRejected?.Invoke(ReadString(payload, "errorCode") ?? "UNKNOWN", ReadString(payload, "message") ?? "Nước đi bị từ chối.", revision);
    }

    private void ParseChallengeSent(JsonElement payload)
    {
        var challengeId = ReadString(payload, "challengeId")
            ?? throw new JsonException("CHALLENGE_SENT thiếu challengeId.");
        var targetDisplayName = ReadString(payload, "targetDisplayName") ?? "đối thủ";
        ChallengeSent?.Invoke(challengeId, targetDisplayName);
    }

    private void ParseQuickChat(JsonElement payload)
    {
        QuickChatReceived?.Invoke(new QuickChatMessage(
            ReadString(payload, "messageId") ?? throw new JsonException("QUICK_CHAT_RECEIVED thiếu messageId."),
            ReadString(payload, "roomId") ?? throw new JsonException("QUICK_CHAT_RECEIVED thiếu roomId."),
            ReadString(payload, "senderPlayerId") ?? string.Empty,
            ReadString(payload, "senderDisplayName") ?? "Kỳ thủ",
            ReadString(payload, "code") ?? string.Empty,
            ReadString(payload, "text") ?? string.Empty,
            payload.TryGetProperty("isSpectator", out var spectator) && spectator.ValueKind == JsonValueKind.True,
            payload.TryGetProperty("sentAtUtc", out var sentAt) && sentAt.TryGetDateTimeOffset(out var timestamp)
                ? timestamp : DateTimeOffset.UtcNow));
    }

    private void ParseWaitingRooms(JsonElement payload)
    {
        var rooms = payload.TryGetProperty("rooms", out var node) ? node : payload;
        WaitingRoomsUpdated?.Invoke(rooms.EnumerateArray().Select(ParseWaitingRoom).ToArray());
    }

    private static WaitingRoomSummary ParseWaitingRoom(JsonElement room) => new(
        ReadString(room, "roomId") ?? string.Empty,
        ReadString(room, "ownerPlayerId") ?? string.Empty,
        ReadString(room, "ownerDisplayName") ?? "Người chơi",
        ReadString(room, "timeProfile") ?? "10+0",
        room.TryGetProperty("createdAtUtc", out var created) && created.TryGetDateTimeOffset(out var value)
            ? value
            : DateTimeOffset.UtcNow,
        room.TryGetProperty("isLocked", out var locked) && locked.ValueKind == JsonValueKind.True);

    private void ParseActiveMatches(JsonElement payload)
    {
        var matches = payload.TryGetProperty("matches", out var node) ? node : payload;
        var result = new List<ActiveMatchSummary>();
        foreach (var match in matches.EnumerateArray())
        {
            result.Add(new ActiveMatchSummary(
                ReadString(match, "roomId") ?? "",
                ReadString(match, "redPlayerId") ?? "",
                ReadString(match, "blackPlayerId") ?? "",
                ReadSide(match, "currentTurn") ?? SideColor.Red,
                ReadString(match, "timeProfile") ?? "60+30",
                match.TryGetProperty("spectatorCount", out var count) ? count.GetInt32() : 0,
                match.TryGetProperty("revision", out var revision) ? revision.GetInt64() : 0,
                ReadString(match, "redDisplayName") ?? "Bên Đỏ",
                ReadString(match, "blackDisplayName") ?? "Bên Đen"));
        }
        ActiveMatchesUpdated?.Invoke(result);
    }

    private void ParseClock(JsonElement payload)
    {
        var node = payload.TryGetProperty("clockState", out var state) ? state : payload;
        ClockSynchronized?.Invoke(ParseClockModel(node));
    }

    private void ParseRepetitionWarning(JsonElement payload)
    {
        var side = ReadSide(payload, "mustVarySide");
        if (side is not null) RepetitionWarningReceived?.Invoke(side.Value, ReadString(payload, "cycleSignature"));
    }

    private void ParseGameEnded(JsonElement payload)
    {
        var result = payload.TryGetProperty("finalResult", out var final) ? final : payload;
        GameEnded?.Invoke(new GameResultSummary(
            ReadString(result, "resultType") ?? "UNKNOWN",
            ReadString(result, "endReason") ?? "UNKNOWN",
            ReadSide(result, "winnerSide"),
            ReadString(result, "explanation") ?? "Trận đấu đã kết thúc."));
    }

    private void ParseRematchOffered(JsonElement payload)
    {
        var requestedBy = ReadString(payload, "requestedBy") ?? throw new JsonException("REMATCH_OFFERED thiếu requestedBy.");
        var targetPlayerId = ReadString(payload, "targetPlayerId") ?? throw new JsonException("REMATCH_OFFERED thiếu targetPlayerId.");
        var expiresAt = payload.TryGetProperty("expiresAtUtc", out var node) && node.ValueKind == JsonValueKind.String &&
                        DateTimeOffset.TryParse(node.GetString(), out var parsed)
            ? parsed : DateTimeOffset.UtcNow.AddSeconds(60);
        RematchOffered?.Invoke(requestedBy, targetPlayerId, expiresAt);
    }

    private void ParseReconnect(JsonElement payload)
    {
        _playerId = ReadString(payload, "playerId") ?? _playerId;
        StartHeartbeat();
        Reconnected?.Invoke();
    }

    private void ParseDrawOffered(JsonElement payload)
    {
        var offeredBy = ReadString(payload, "offeredBy") ?? string.Empty;
        var expiresAt = payload.TryGetProperty("expiresAt", out var node) && node.TryGetDateTimeOffset(out var parsed)
            ? parsed : DateTimeOffset.UtcNow.AddSeconds(30);
        DrawOffered?.Invoke(offeredBy, expiresAt);
    }

    private void ParseHistory(JsonElement payload)
    {
        var result = new List<MatchHistorySummary>();
        var matches = payload.TryGetProperty("matches", out var node) ? node : payload;
        foreach (var match in matches.EnumerateArray())
        {
            var started = match.TryGetProperty("startedAtUtc", out var start) && start.TryGetDateTimeOffset(out var parsed)
                ? parsed : DateTimeOffset.MinValue;
            result.Add(new MatchHistorySummary(
                ReadString(match, "matchId") ?? "",
                ReadString(match, "roomId") ?? "",
                ReadString(match, "status") ?? "UNKNOWN",
                ReadString(match, "resultType") ?? "Đang chơi",
                ReadString(match, "endReason") ?? "",
                match.TryGetProperty("totalMoves", out var total) ? total.GetInt32() : 0,
                started,
                match.TryGetProperty("endedAtUtc", out var ended) && ended.ValueKind != JsonValueKind.Null && ended.TryGetDateTimeOffset(out var endedAt) ? endedAt : null,
                ReadString(match, "winnerSide"),
                ReadString(match, "timeProfile") ?? "10+0",
                ReadString(match, "viewerSide") ?? "SPECTATOR",
                ReadString(match, "redDisplayName") ?? "Bên Đỏ",
                ReadString(match, "blackDisplayName") ?? "Bên Đen"));
        }
        HistoryUpdated?.Invoke(result);
    }

    private void ParseHistoryDetail(JsonElement payload)
    {
        var match = payload.GetProperty("match");
        var frames = new List<ReplayFrame>
        {
            new(0, SideColor.Red, InitialBoard.Create(), null, null, "Bắt đầu ván đấu")
        };
        var moveNodes = payload.TryGetProperty("moves", out var movesNode) && movesNode.ValueKind == JsonValueKind.Array
            ? movesNode.EnumerateArray().OrderBy(item => item.GetProperty("revision").GetInt64()).ToArray()
            : [];
        var movesByRevision = moveNodes.ToDictionary(item => item.GetProperty("revision").GetInt64());

        if (payload.TryGetProperty("positions", out var positions) && positions.ValueKind == JsonValueKind.Array && positions.GetArrayLength() > 0)
        {
            foreach (var position in positions.EnumerateArray().OrderBy(item => item.GetProperty("revision").GetInt64()))
            {
                var revision = position.GetProperty("revision").GetInt64();
                var mapJson = ReadString(position, "canonicalPieceMapJson") ?? throw new JsonException("Thiếu dữ liệu thế cờ.");
                using var map = JsonDocument.Parse(mapJson);
                var turn = Enum.Parse<SideColor>(ReadString(map.RootElement, "turn") ?? ReadString(position, "sideToMove") ?? "RED", true);
                var pieces = map.RootElement.GetProperty("pieces").EnumerateArray().Select(piece => new PieceState(
                    ReadString(piece, "id") ?? "UNKNOWN",
                    Enum.Parse<SideColor>(ReadString(piece, "side")!, true),
                    Enum.Parse<PieceType>(ReadString(piece, "type")!, true),
                    new Position(piece.GetProperty("x").GetInt32(), piece.GetProperty("y").GetInt32()))).ToArray();
                movesByRevision.TryGetValue(revision, out var move);
                var from = move.ValueKind == JsonValueKind.Object ? ReadBoardPosition(move, "from") : null;
                var to = move.ValueKind == JsonValueKind.Object ? ReadBoardPosition(move, "to") : null;
                frames.Add(new ReplayFrame(revision, turn, pieces, from, to, DescribeReplayMove(move, revision)));
            }
        }
        else
        {
            var rebuilt = InitialBoard.Create().ToList();
            foreach (var move in moveNodes)
            {
                var pieceId = ReadString(move, "pieceId") ?? throw new JsonException("Nước đi lịch sử thiếu mã quân cờ.");
                var movingIndex = rebuilt.FindIndex(piece => piece.PieceId == pieceId);
                if (movingIndex < 0) throw new JsonException($"Không tìm thấy quân {pieceId} khi dựng lịch sử.");
                var from = ReadBoardPosition(move, "from");
                var to = ReadBoardPosition(move, "to") ?? throw new JsonException("Nước đi lịch sử thiếu ô đích.");
                var capturedId = ReadString(move, "capturedPieceId");
                rebuilt.RemoveAll(piece => piece.PieceId == capturedId || (piece.Position == to && piece.PieceId != pieceId));
                movingIndex = rebuilt.FindIndex(piece => piece.PieceId == pieceId);
                var movingSide = rebuilt[movingIndex].Side;
                rebuilt[movingIndex] = rebuilt[movingIndex] with { Position = to };
                var revision = move.GetProperty("revision").GetInt64();
                var turn = movingSide == SideColor.Red ? SideColor.Black : SideColor.Red;
                frames.Add(new ReplayFrame(revision, turn, rebuilt.ToArray(), from, to, DescribeReplayMove(move, revision)));
            }
        }

        var finalFrame = frames[^1];
        var redPlayerId = ReadString(match, "redPlayerId");
        var blackPlayerId = ReadString(match, "blackPlayerId");
        SideColor? viewerSide = string.Equals(_playerId, redPlayerId, StringComparison.Ordinal) ? SideColor.Red
            : string.Equals(_playerId, blackPlayerId, StringComparison.Ordinal) ? SideColor.Black
            : null;
        var resultType = ReadString(match, "resultType") ?? "FINISHED";
        var resultLabel = resultType.Equals("DRAW", StringComparison.OrdinalIgnoreCase) ? "Ván hòa"
            : (viewerSide == SideColor.Red && resultType.Equals("RED_WIN", StringComparison.OrdinalIgnoreCase)) ||
              (viewerSide == SideColor.Black && resultType.Equals("BLACK_WIN", StringComparison.OrdinalIgnoreCase)) ? "Bạn thắng"
            : resultType.EndsWith("_WIN", StringComparison.OrdinalIgnoreCase) ? "Bạn thua" : "Ván đã kết thúc";
        SnapshotReceived?.Invoke(new GameSnapshot(
            ReadString(match, "roomId") ?? "REPLAY",
            finalFrame.Revision,
            finalFrame.CurrentTurn,
            finalFrame.Pieces, "FINISHED", "REPLAY"));
        ReplayTimelineLoaded?.Invoke(new ReplaySession(
            ReadString(match, "matchId") ?? string.Empty,
            ReadString(match, "roomId") ?? "REPLAY",
            viewerSide,
            resultLabel,
            frames));
        ReplayLoaded?.Invoke();
    }

    private static Position? ReadBoardPosition(JsonElement node, string property)
    {
        if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Object)
            return null;
        return new Position(value.GetProperty("x").GetInt32(), value.GetProperty("y").GetInt32());
    }

    private static string DescribeReplayMove(JsonElement move, long revision)
    {
        if (move.ValueKind != JsonValueKind.Object) return $"Nước {revision}";
        var side = ReadString(move, "side")?.Equals("RED", StringComparison.OrdinalIgnoreCase) == true ? "Đỏ" : "Đen";
        var piece = ReadString(move, "pieceType") ?? ReadString(move, "pieceId") ?? "quân";
        var from = ReadBoardPosition(move, "from");
        var to = ReadBoardPosition(move, "to");
        var action = ReadString(move, "capturedPieceId") is null ? "đi" : "ăn quân";
        return $"Nước {revision}: {side} · {piece} {action} {from} → {to}";
    }

    private void StartHeartbeat()
    {
        StopHeartbeat();
        var cts = new CancellationTokenSource();
        _heartbeatCts = cts;
        _ = Task.Run(async () =>
        {
            try
            {
                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
                while (await timer.WaitForNextTickAsync(cts.Token).ConfigureAwait(false))
                    await SendAsync("PING", new { clientTimeUtc = DateTimeOffset.UtcNow }, true, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { ErrorReceived?.Invoke($"Heartbeat thất bại: {ex.Message}"); }
        });
    }

    private void StopHeartbeat()
    {
        var cts = Interlocked.Exchange(ref _heartbeatCts, null);
        if (cts is null) return;
        cts.Cancel();
        cts.Dispose();
    }

    private static ClockSnapshotModel ParseClockModel(JsonElement node) => new(
        node.TryGetProperty("redRemainingMs", out var red) ? red.GetInt64() : 0,
        node.TryGetProperty("blackRemainingMs", out var black) ? black.GetInt64() : 0,
        ReadSide(node, "activeSide") ?? SideColor.Red,
        node.TryGetProperty("incrementMs", out var increment) ? increment.GetInt64() : 0,
        node.TryGetProperty("serverAnchorUtc", out var anchor) && anchor.TryGetDateTimeOffset(out var timestamp)
            ? timestamp : DateTimeOffset.UtcNow,
        node.TryGetProperty("isExpired", out var expired) && expired.GetBoolean());

    private static string? ReadString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;

    private static SideColor? ReadSide(JsonElement element, string property)
        => ReadEnum<SideColor>(element, property);

    private static TEnum? ReadEnum<TEnum>(JsonElement element, string property) where TEnum : struct, Enum
    {
        if (!element.TryGetProperty(property, out var value)) return null;
        if (value.ValueKind == JsonValueKind.String &&
            Enum.TryParse<TEnum>(value.GetString(), true, out var textValue)) return textValue;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) &&
            Enum.IsDefined(typeof(TEnum), number)) return (TEnum)Enum.ToObject(typeof(TEnum), number);
        return null;
    }
}
