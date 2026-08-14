using System.Text.Json;
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

    public GameClient(IProtocolTransport transport)
    {
        _transport = transport;
        _transport.MessageHandler = HandleMessageAsync;
        _transport.StateChanged += (state, error) => ConnectionChanged?.Invoke(state, error);
    }

    public event Action<ConnectionState, string?>? ConnectionChanged;
    public event Action<string, string>? LoginCompleted;
    public event Action<IReadOnlyList<PlayerSummary>>? PlayersUpdated;
    public event Action<ChallengeSummary>? ChallengeReceived;
    public event Action<string>? ChallengeRejected;
    public event Action<string>? RoomCreated;
    public event Action<GameSnapshot>? SnapshotReceived;
    public event Action<long, MoveDelta>? MoveCommitted;
    public event Action<string, string, long>? MoveRejected;
    public event Action<string>? ErrorReceived;

    public ConnectionState State => _transport.State;

    public async Task ConnectAndLoginAsync(string host, int port, string displayName, CancellationToken cancellationToken)
    {
        _helloAck = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await _transport.ConnectAsync(host, port, cancellationToken);
            await SendAsync("HELLO", new { protocolVersion = ProtocolConstants.ProtocolVersion, clientName = "UDM18.WPF" }, false, cancellationToken);
            await _helloAck.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            await SendAsync("LOGIN_REQUEST", new { displayName, resumeToken = (string?)null }, false, cancellationToken);
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
        => SendAsync("CHALLENGE_SEND", new { targetPlayerId, timeProfile = "STANDARD_PRO" }, true, cancellationToken);

    public Task AcceptChallengeAsync(string challengeId, CancellationToken cancellationToken = default)
        => SendAsync("CHALLENGE_ACCEPT", new { challengeId }, true, cancellationToken);

    public Task RejectChallengeAsync(string challengeId, CancellationToken cancellationToken = default)
        => SendAsync("CHALLENGE_REJECT", new { challengeId, reason = "USER_REJECTED" }, true, cancellationToken);

    public Task SendMoveAsync(string roomId, long expectedRevision, Position from, Position to, CancellationToken cancellationToken = default)
        => SendAsync("MOVE_REQUEST", new { clientMoveId = UlidId.New(), expectedRevision, from, to }, true, cancellationToken, roomId);

    public Task RequestResyncAsync(string roomId, long lastRevision, CancellationToken cancellationToken = default)
        => SendAsync("RESYNC_REQUEST", new { roomId, lastRevision }, true, cancellationToken, roomId);

    public Task DisconnectAsync() => _transport.DisconnectAsync();

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
                case "LOGIN_RESULT": if (ParseLogin(payload)) await RequestPlayersAsync(); break;
                case "PLAYER_LIST_UPDATED": ParsePlayers(payload); break;
                case "CHALLENGE_RECEIVED": ParseChallenge(payload); break;
                case "CHALLENGE_REJECTED": ParseChallengeRejected(payload); break;
                case "ROOM_CREATED": ParseRoom(payload); break;
                case "GAME_STATE_SNAPSHOT": SnapshotReceived?.Invoke(ParseSnapshot(payload)); break;
                case "MOVE_COMMITTED": ParseMoveCommitted(root, payload); break;
                case "MOVE_REJECTED": ParseMoveRejected(root, payload); break;
                case "ERROR_RESPONSE": ErrorReceived?.Invoke(ReadString(payload, "message") ?? "Server báo lỗi."); break;
            }
        }
        catch (Exception ex)
        {
            ErrorReceived?.Invoke($"Không đọc được event {type}: {ex.Message}");
        }
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
            ErrorReceived?.Invoke($"Đăng nhập thất bại: {status}");
            return false;
        }
        _sessionToken = ReadString(payload, "token") ?? throw new JsonException("LOGIN_RESULT thiếu token.");
        var player = payload.GetProperty("player");
        LoginCompleted?.Invoke(ReadString(player, "playerId") ?? "", ReadString(player, "displayName") ?? "");
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
                Enum.Parse<SideColor>(ReadString(piece, "side")!, true),
                Enum.Parse<PieceType>(ReadString(piece, "type")!, true),
                new Position(piece.GetProperty("x").GetInt32(), piece.GetProperty("y").GetInt32())));
        }
        return new GameSnapshot(
            ReadString(payload, "roomId") ?? "",
            payload.GetProperty("revision").GetInt64(),
            Enum.Parse<SideColor>(ReadString(payload, "currentTurn")!, true),
            parsed,
            ReadString(payload, "status") ?? "PLAYING");
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
            currentTurn));
    }

    private void ParseMoveRejected(JsonElement root, JsonElement payload)
    {
        var revision = payload.TryGetProperty("revision", out var r) ? r.GetInt64()
            : root.TryGetProperty("revision", out r) ? r.GetInt64() : 0;
        MoveRejected?.Invoke(ReadString(payload, "errorCode") ?? "UNKNOWN", ReadString(payload, "message") ?? "Nước đi bị từ chối.", revision);
    }

    private static string? ReadString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind != JsonValueKind.Null ? value.GetString() : null;

    private static SideColor? ReadSide(JsonElement element, string property)
        => Enum.TryParse<SideColor>(ReadString(element, property), true, out var side) ? side : null;
}
