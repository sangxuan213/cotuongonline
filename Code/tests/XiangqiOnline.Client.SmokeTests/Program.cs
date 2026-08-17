using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using UDM18.Client.Models;
using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;
using XiangqiOnline.Shared.Protocol;
using UDM18.Client.Protocol;
using UDM18.Client.ViewModels;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Initial board has 32 stable pieces", TestInitialBoard),
    ("Coordinate mapping preserves canonical protocol", TestCoordinateMapping),
    ("ULID identifiers are valid and unique", TestUlids),
    ("TCP framing handles fragmented server messages", TestTcpFraming),
    ("Real TV1 framing completes HELLO and LOGIN handshake", TestRealWireHandshake),
    ("Login waits for HELLO_ACK", TestHandshakeOrder),
    ("Challenge uses STANDARD_PRO contract profile", TestChallengeProfile),
    ("Rejected challenge clears stale incoming challenge", TestChallengeRejectedClearsIncoming),
    ("Only current-turn pieces can be selected", TestSourceSelection),
    ("Board changes only after MOVE_COMMITTED", TestAuthoritativeMoveFlow),
    ("Committed capture removes only target", TestCommittedCapture),
    ("MOVE_REJECTED preserves board", TestRejectedMove),
    ("Unknown piece delta preserves board revision", TestUnknownPieceDelta),
    ("Revision gap preserves board and requests resync", TestRevisionGap),
    ("Snapshot clears stale move highlights", TestSnapshotClearsHighlights),
    ("Older snapshots cannot overwrite current state", TestOldSnapshot),
    ("Malformed events report errors without crashing", TestMalformedEvent),
    ("ERROR_RESPONSE is surfaced without disconnecting", TestErrorResponse)
};

var failures = 0;
foreach (var test in tests)
{
    try { await test.Run(); Console.WriteLine($"PASS  {test.Name}"); }
    catch (Exception ex) { failures++; Console.WriteLine($"FAIL  {test.Name}: {ex.Message}"); }
}
Console.WriteLine($"\n{tests.Length - failures}/{tests.Length} smoke tests passed.");
return failures == 0 ? 0 : 1;

static Task TestInitialBoard()
{
    var pieces = InitialBoard.Create();
    Check(pieces.Count == 32, "Expected 32 pieces.");
    Check(pieces.Select(p => p.PieceId).Distinct().Count() == 32, "Piece IDs must be unique.");
    Check(pieces.All(p => p.Position.IsValid()), "Every piece must be on board.");
    Check(pieces.Any(p => p.PieceId == "RED_GENERAL") && pieces.Any(p => p.PieceId == "BLACK_GENERAL"), "General IDs must match the baseline contract.");
    Check(pieces.All(p => p.PieceId is not "RED_GENERAL_1" and not "BLACK_GENERAL_1"), "Numbered general IDs are forbidden.");
    return Task.CompletedTask;
}

static Task TestCoordinateMapping()
{
    var canonical = new Position(1, 9);
    Check(BoardGeometry.CanonicalToView(canonical, BoardOrientation.RedAtBottom) == canonical, "Red view changed coordinate.");
    var blackView = BoardGeometry.CanonicalToView(canonical, BoardOrientation.BlackAtBottom);
    Check(blackView == new Position(7, 0), "Black rotation incorrect.");
    Check(BoardGeometry.ViewToCanonical(blackView.X, blackView.Y, BoardOrientation.BlackAtBottom) == canonical, "Round trip failed.");
    return Task.CompletedTask;
}

static Task TestUlids()
{
    var ids = Enumerable.Range(0, 100).Select(_ => UlidId.New()).ToArray();
    Check(ids.All(id => id.Length == 26), "ULID length must be 26.");
    Check(ids.Distinct().Count() == ids.Length, "ULIDs must be unique.");
    return Task.CompletedTask;
}

static async Task TestTcpFraming()
{
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var port = ((IPEndPoint)listener.LocalEndpoint).Port;
    await using var transport = new TcpProtocolTransport();
    var received = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
    transport.MessageHandler = message => { received.TrySetResult(message); return Task.CompletedTask; };
    var acceptTask = listener.AcceptTcpClientAsync();
    await transport.ConnectAsync("127.0.0.1", port, CancellationToken.None);
    using var server = await acceptTask;
    var stream = server.GetStream();

    await transport.SendAsync(new { protocolVersion = "1.0", type = "PING", payload = new { nonce = "N1" } });
    var header = new byte[4];
    await stream.ReadExactlyAsync(header);
    var unsignedLength = BinaryPrimitives.ReadUInt32BigEndian(header);
    Check(unsignedLength is > 0 and <= TcpProtocolTransport.MaxPayloadBytes, "Invalid outbound frame length.");
    var length = (int)unsignedLength;
    var request = new byte[length];
    await stream.ReadExactlyAsync(request);
    using (var doc = JsonDocument.Parse(request)) Check(doc.RootElement.GetProperty("type").GetString() == "PING", "Outbound JSON incorrect.");

    var eventBytes = Encoding.UTF8.GetBytes("{\"type\":\"PONG\",\"payload\":{\"nonce\":\"N1\"}}");
    BinaryPrimitives.WriteUInt32BigEndian(header, (uint)eventBytes.Length);
    await stream.WriteAsync(header.AsMemory(0, 1));
    await stream.WriteAsync(header.AsMemory(1, 3));
    for (var offset = 0; offset < eventBytes.Length; offset += 3)
        await stream.WriteAsync(eventBytes.AsMemory(offset, Math.Min(3, eventBytes.Length - offset)));
    var message = await received.Task.WaitAsync(TimeSpan.FromSeconds(2));
    Check(message.GetProperty("type").GetString() == "PONG", "Fragmented event was not decoded.");
    await transport.DisconnectAsync();
    listener.Stop();
}

static async Task TestHandshakeOrder()
{
    var transport = new FakeTransport();
    var client = new GameClient(transport);
    var loginTask = client.ConnectAndLoginAsync("127.0.0.1", 18180, "Tester", CancellationToken.None);
    Check(transport.SentTypes.SequenceEqual(["HELLO"]), "LOGIN_REQUEST was sent before HELLO_ACK.");
    await transport.EmitAsync(Json("""{"type":"HELLO_ACK","payload":{"supportedVersion":"1.0","serverId":"S1"}}"""));
    await loginTask;
    Check(transport.SentTypes.SequenceEqual(["HELLO", "LOGIN_REQUEST"]), "LOGIN_REQUEST was not sent after HELLO_ACK.");
    await transport.EmitAsync(Json("""{"type":"LOGIN_RESULT","payload":{"status":"ACCEPTED","token":"TOKEN","player":{"playerId":"P1","displayName":"Tester"}}}"""));
    Check(transport.SentTypes.SequenceEqual(["HELLO", "LOGIN_REQUEST", "PLAYER_LIST_REQUEST"]), "Player list was not requested after login.");
}

static async Task TestRealWireHandshake()
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var port = ((IPEndPoint)listener.LocalEndpoint).Port;
    await using var transport = new TcpProtocolTransport();
    var client = new GameClient(transport);
    var acceptTask = listener.AcceptTcpClientAsync(timeout.Token).AsTask();
    var loginTask = client.ConnectAndLoginAsync("127.0.0.1", port, "Tester", timeout.Token);
    using var server = await acceptTask;
    var stream = server.GetStream();

    var helloBytes = await TcpFrameCodec.ReadFrameAsync(stream, timeout.Token);
    Check(helloBytes is not null, "TV1 codec did not receive HELLO.");
    using (var hello = JsonDocument.Parse(helloBytes!))
    {
        Check(hello.RootElement.GetProperty("protocolVersion").GetString() == ProtocolConstants.ProtocolVersion, "HELLO envelope version mismatch.");
        Check(hello.RootElement.GetProperty("type").GetString() == "HELLO", "First real-wire request was not HELLO.");
        Check(hello.RootElement.GetProperty("clientSequence").GetInt64() == 1, "HELLO clientSequence mismatch.");
    }

    var ack = JsonSerializer.SerializeToUtf8Bytes(new ServerEventEnvelope<object>
    {
        Type = "HELLO_ACK",
        EventId = UlidId.New(),
        ServerSequence = 1,
        ServerTimeUtc = DateTimeOffset.UtcNow,
        Payload = new { supportedVersion = ProtocolConstants.ProtocolVersion, serverId = "TV1-TEST" }
    }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    await TcpFrameCodec.WriteFrameAsync(stream, ack, timeout.Token);
    await loginTask;

    var loginBytes = await TcpFrameCodec.ReadFrameAsync(stream, timeout.Token);
    Check(loginBytes is not null, "TV1 codec did not receive LOGIN_REQUEST.");
    using (var login = JsonDocument.Parse(loginBytes!))
    {
        Check(login.RootElement.GetProperty("type").GetString() == "LOGIN_REQUEST", "HELLO_ACK did not release LOGIN_REQUEST.");
        Check(login.RootElement.GetProperty("clientSequence").GetInt64() == 2, "LOGIN_REQUEST clientSequence mismatch.");
    }

    await transport.DisconnectAsync();
    listener.Stop();
}

static async Task TestChallengeProfile()
{
    var transport = new FakeTransport();
    var client = new GameClient(transport);
    await client.SendChallengeAsync("P2");
    var payload = transport.LastSent!.Value.GetProperty("payload");
    Check(payload.GetProperty("timeProfile").GetString() == "STANDARD_PRO", "Challenge profile does not match the contract.");
}

static async Task TestChallengeRejectedClearsIncoming()
{
    var transport = new FakeTransport();
    var client = new GameClient(transport);
    var lobby = new LobbyViewModel(client);

    await transport.EmitAsync(Json("""{"type":"CHALLENGE_RECEIVED","payload":{"challenge":{"challengeId":"C1","fromPlayerId":"P1","fromDisplayName":"Alice"}}}"""));
    Check(lobby.IncomingChallenge?.ChallengeId == "C1", "Incoming challenge was not created.");
    Check(lobby.AcceptCommand.CanExecute(null), "Accept must be enabled for an incoming challenge.");
    Check(lobby.RejectCommand.CanExecute(null), "Reject must be enabled for an incoming challenge.");

    await transport.EmitAsync(Json("""{"type":"CHALLENGE_REJECTED","payload":{"challengeId":"C1","rejectedByPlayerId":"P1","status":"REJECTED"}}"""));
    Check(lobby.IncomingChallenge is null, "Rejected challenge remained visible.");
    Check(!lobby.AcceptCommand.CanExecute(null), "Accept remained enabled after rejection.");
    Check(!lobby.RejectCommand.CanExecute(null), "Reject remained enabled after rejection.");
}

static async Task TestSourceSelection()
{
    var (transport, vm) = CreateGame();
    await transport.EmitAsync(RoomCreated("ROOM-1"));
    await transport.EmitAsync(Snapshot("ROOM-1", 1));
    vm.CoordinateClickedCommand.Execute(new Position(4, 4));
    Check(vm.Selected is null, "Empty source square was selected.");
    vm.CoordinateClickedCommand.Execute(new Position(0, 0));
    Check(vm.Selected is null, "Opponent piece was selected.");
    vm.CoordinateClickedCommand.Execute(new Position(1, 9));
    Check(vm.Selected == new Position(1, 9), "Current-turn piece was not selected.");
}

static async Task TestAuthoritativeMoveFlow()
{
    var (transport, vm) = CreateGame();
    await transport.EmitAsync(RoomCreated("ROOM-1"));
    await transport.EmitAsync(Snapshot("ROOM-1", 1));
    var before = vm.Pieces.Single(p => p.PieceId == "RED_HORSE_1").Position;
    vm.CoordinateClickedCommand.Execute(before);
    vm.CoordinateClickedCommand.Execute(new Position(2, 7));
    Check(vm.Pieces.Single(p => p.PieceId == "RED_HORSE_1").Position == before, "Client mutated board before commit.");
    Check(transport.LastSent?.GetProperty("type").GetString() == "MOVE_REQUEST", "MOVE_REQUEST was not sent.");
    Check(vm.IsMovePending, "Pending guard was not enabled.");
    await transport.EmitAsync(MoveCommitted(2));
    Check(vm.Pieces.Single(p => p.PieceId == "RED_HORSE_1").Position == new Position(2, 7), "Committed move not applied.");
    Check(vm.Revision == 2 && !vm.IsMovePending, "Revision/pending state incorrect.");
    Check(vm.CurrentTurn == SideColor.Black, "Current turn was not synchronized from the committed event.");
}

static async Task TestCommittedCapture()
{
    var (transport, vm) = CreateGame();
    await transport.EmitAsync(RoomCreated("ROOM-1"));
    await transport.EmitAsync(Snapshot("ROOM-1", 1));
    await transport.EmitAsync(Json("""
      {"type":"MOVE_COMMITTED","revision":2,"payload":{"side":"RED","pieceId":"RED_CHARIOT_1","from":{"x":0,"y":9},"to":{"x":0,"y":3},"capturedPieceId":"BLACK_PAWN_1","clocks":{"activeSide":"BLACK"}}}
      """));
    Check(vm.Pieces.All(p => p.PieceId != "BLACK_PAWN_1"), "Captured target remains on board.");
    Check(vm.Pieces.Single(p => p.PieceId == "RED_CHARIOT_1").Position == new Position(0, 3), "Capturing piece did not move.");
    Check(vm.Pieces.Count == 31 && vm.Revision == 2, "Capture state is inconsistent.");
}

static async Task TestRejectedMove()
{
    var (transport, vm) = CreateGame();
    await transport.EmitAsync(RoomCreated("ROOM-1"));
    await transport.EmitAsync(Snapshot("ROOM-1", 4));
    var before = vm.Pieces.Select(p => (p.PieceId, p.Position)).ToArray();
    vm.CoordinateClickedCommand.Execute(new Position(0, 6));
    vm.CoordinateClickedCommand.Execute(new Position(0, 5));
    await transport.EmitAsync(Json("""
      {"type":"MOVE_REJECTED","payload":{"errorCode":"NOT_YOUR_TURN","message":"Không đúng lượt","revision":4}}
      """));
    Check(before.SequenceEqual(vm.Pieces.Select(p => (p.PieceId, p.Position))), "Rejected move changed board.");
    Check(!vm.IsMovePending && vm.Revision == 4, "Rejected state did not clear pending safely.");
}

static async Task TestUnknownPieceDelta()
{
    var (transport, vm) = CreateGame();
    await transport.EmitAsync(RoomCreated("ROOM-1"));
    await transport.EmitAsync(Snapshot("ROOM-1", 4));
    var before = vm.Pieces.Select(p => (p.PieceId, p.Position)).ToArray();
    await transport.EmitAsync(Json("""
      {"type":"MOVE_COMMITTED","revision":5,"payload":{"pieceId":"MISSING","from":{"x":0,"y":0},"to":{"x":0,"y":1},"capturedPieceId":null}}
      """));
    Check(vm.Revision == 4, "Unknown delta advanced revision.");
    Check(before.SequenceEqual(vm.Pieces.Select(p => (p.PieceId, p.Position))), "Unknown delta changed board.");
    Check(transport.SentTypes.Last() == "RESYNC_REQUEST", "Unknown piece did not request resync.");
}

static async Task TestRevisionGap()
{
    var (transport, vm) = CreateGame();
    await transport.EmitAsync(RoomCreated("ROOM-1"));
    await transport.EmitAsync(Snapshot("ROOM-1", 4));
    var before = vm.Pieces.Select(p => (p.PieceId, p.Position)).ToArray();
    await transport.EmitAsync(MoveCommitted(7));
    Check(vm.Revision == 4, "Revision gap advanced local revision.");
    Check(before.SequenceEqual(vm.Pieces.Select(p => (p.PieceId, p.Position))), "Revision gap changed board.");
    Check(transport.SentTypes.Last() == "RESYNC_REQUEST", "Revision gap did not request resync.");
}

static async Task TestSnapshotClearsHighlights()
{
    var (transport, vm) = CreateGame();
    await transport.EmitAsync(RoomCreated("ROOM-1"));
    await transport.EmitAsync(Snapshot("ROOM-1", 1));
    await transport.EmitAsync(MoveCommitted(2));
    Check(vm.LastFrom is not null && vm.LastTo is not null, "Committed move was not highlighted.");
    await transport.EmitAsync(Snapshot("ROOM-1", 3));
    Check(vm.LastFrom is null && vm.LastTo is null, "Snapshot retained stale highlights.");
}

static async Task TestOldSnapshot()
{
    var (transport, vm) = CreateGame();
    await transport.EmitAsync(RoomCreated("ROOM-1"));
    await transport.EmitAsync(Snapshot("ROOM-1", 4));
    await transport.EmitAsync(Snapshot("ROOM-1", 3));
    Check(vm.Revision == 4, "Older snapshot overwrote the current revision.");
}

static async Task TestMalformedEvent()
{
    var transport = new FakeTransport();
    var client = new GameClient(transport);
    string? error = null;
    client.ErrorReceived += message => error = message;
    await transport.EmitAsync(Json("""{"type":"MOVE_COMMITTED","revision":2,"payload":{"pieceId":"RED_HORSE_1"}}"""));
    Check(!string.IsNullOrWhiteSpace(error), "Malformed event was not reported.");
    Check(transport.State == ConnectionState.Connected, "Malformed event closed the connection.");
}

static async Task TestErrorResponse()
{
    var transport = new FakeTransport();
    var client = new GameClient(transport);
    string? error = null;
    client.ErrorReceived += message => error = message;
    await transport.EmitAsync(Json("""{"type":"ERROR_RESPONSE","payload":{"errorCode":"INVALID_REQUEST","message":"Invalid request"}}"""));
    Check(error == "Invalid request", "ERROR_RESPONSE message was not surfaced.");
    Check(transport.State == ConnectionState.Connected, "ERROR_RESPONSE closed the connection.");
}

static (FakeTransport, GameRoomViewModel) CreateGame()
{
    var transport = new FakeTransport();
    var client = new GameClient(transport);
    return (transport, new GameRoomViewModel(client));
}

static JsonElement RoomCreated(string roomId) => JsonSerializer.SerializeToElement(new
{
    type = "ROOM_CREATED",
    payload = new { roomId }
});

static JsonElement Snapshot(string roomId, long revision)
{
    var pieces = InitialBoard.Create().Select(p => new
    {
        pieceId = p.PieceId,
        side = p.Side.ToString().ToUpperInvariant(),
        type = p.Type.ToString().ToUpperInvariant(),
        x = p.Position.X,
        y = p.Position.Y,
        captured = false
    }).ToArray();
    return JsonSerializer.SerializeToElement(new
    {
        type = "GAME_STATE_SNAPSHOT",
        payload = new { roomId, revision, currentTurn = "RED", status = "PLAYING", pieces }
    });
}

static JsonElement MoveCommitted(long revision) => JsonSerializer.SerializeToElement(new
{
    type = "MOVE_COMMITTED",
    revision,
    payload = new
    {
        pieceId = "RED_HORSE_1",
        side = "RED",
        from = new { x = 1, y = 9 },
        to = new { x = 2, y = 7 },
        capturedPieceId = (string?)null,
        clocks = new { activeSide = "BLACK" }
    }
});

static JsonElement Json(string json)
{
    using var doc = JsonDocument.Parse(json);
    return doc.RootElement.Clone();
}

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed class FakeTransport : IProtocolTransport
{
    public ConnectionState State { get; private set; } = ConnectionState.Connected;
    public event Action<ConnectionState, string?>? StateChanged;
    public Func<JsonElement, Task>? MessageHandler { get; set; }
    public JsonElement? LastSent { get; private set; }
    public List<string> SentTypes { get; } = [];
    public Task ConnectAsync(string host, int port, CancellationToken cancellationToken) { State = ConnectionState.Connected; StateChanged?.Invoke(State, null); return Task.CompletedTask; }
    public Task DisconnectAsync() { State = ConnectionState.Disconnected; StateChanged?.Invoke(State, null); return Task.CompletedTask; }
    public Task SendAsync(object envelope, CancellationToken cancellationToken = default)
    {
        LastSent = JsonSerializer.SerializeToElement(envelope, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        SentTypes.Add(LastSent.Value.GetProperty("type").GetString() ?? "");
        return Task.CompletedTask;
    }
    public async Task EmitAsync(JsonElement message) { if (MessageHandler is not null) await MessageHandler(message); }
    public void Abort() { }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
