using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text.Json;
using XiangqiOnline.IntegrationTests.Fixtures;
using XiangqiOnline.Server.Lobby;
using XiangqiOnline.Server.Networking;

namespace XiangqiOnline.IntegrationTests;

public sealed class BusinessCriticalFlowTests
{
    [Fact]
    public async Task PublicMatch_SpectatorDrawAndHistory_WorkOverRealTcp()
    {
        using var database = TestDatabase.Create();
        var (server, players, challenges) = CreateServer(database);
        await using var guard = server;
        await server.StartAsync();

        using var redClient = await ConnectAsync(server);
        using var blackClient = await ConnectAsync(server);
        using var spectatorClient = await ConnectAsync(server);
        var red = redClient.GetStream();
        var black = blackClient.GetStream();
        var spectator = spectatorClient.GetStream();
        var redLogin = await LoginAsync(red, "Business-Red");
        var blackLogin = await LoginAsync(black, "Business-Black");
        var spectatorLogin = await LoginAsync(spectator, "Business-Viewer");
        var roomId = await CreateAndJoinRoomAsync(red, redLogin.Token, black, blackLogin.Token);

        await SendAsync(spectator, Envelope("SPECTATOR_JOIN", "watch", spectatorLogin.Token, roomId, 3, new { roomId }));
        var watchSnapshot = await ReadUntilAsync(spectator, "GAME_STATE_SNAPSHOT");
        Assert.Equal("SPECTATOR", watchSnapshot.GetProperty("payload").GetProperty("viewerRole").GetString());
        Assert.Equal(1, watchSnapshot.GetProperty("payload").GetProperty("spectatorCount").GetInt32());

        await SendAsync(spectator, Envelope("SPECTATOR_LEAVE", "leave-watch", spectatorLogin.Token, roomId, 4, new { roomId }));
        Assert.Equal("SPECTATOR_LEFT", (await ReadUntilAsync(spectator, "SPECTATOR_LEFT")).GetProperty("type").GetString());

        await SendAsync(red, Envelope("DRAW_OFFER", "draw-1", redLogin.Token, roomId, 4, new { }));
        Assert.Equal(redLogin.PlayerId, (await ReadUntilAsync(black, "DRAW_OFFERED")).GetProperty("payload").GetProperty("offeredBy").GetString());
        await ReadUntilAsync(red, "DRAW_OFFERED");
        await SendAsync(black, Envelope("DRAW_RESPONSE", "draw-no", blackLogin.Token, roomId, 4, new { accept = false }));
        await ReadUntilAsync(red, "DRAW_DECLINED");
        await ReadUntilAsync(black, "DRAW_DECLINED");

        await SendAsync(red, Envelope("DRAW_OFFER", "draw-2", redLogin.Token, roomId, 5, new { }));
        await ReadUntilAsync(red, "DRAW_OFFERED");
        await ReadUntilAsync(black, "DRAW_OFFERED");
        await SendAsync(black, Envelope("DRAW_RESPONSE", "draw-yes", blackLogin.Token, roomId, 5, new { accept = true }));
        var redEnded = await ReadUntilAsync(red, "GAME_ENDED");
        var blackEnded = await ReadUntilAsync(black, "GAME_ENDED");
        Assert.Equal("DRAW", redEnded.GetProperty("payload").GetProperty("finalResult").GetProperty("resultType").GetString());
        Assert.Equal("DRAW_AGREEMENT", blackEnded.GetProperty("payload").GetProperty("finalResult").GetProperty("endReason").GetString());
        Assert.All(players.GetSnapshot().Where(player => player.PlayerId != spectatorLogin.PlayerId),
            player => Assert.Equal(PlayerStatus.AVAILABLE, player.Status));

        await SendAsync(red, Envelope("HISTORY_LIST_REQUEST", "history", redLogin.Token, null, 6, new { }));
        var history = await ReadUntilAsync(red, "HISTORY_LIST_RESULT");
        var match = history.GetProperty("payload").GetProperty("matches").EnumerateArray().Single();
        Assert.Equal(roomId, match.GetProperty("roomId").GetString());
        var matchId = match.GetProperty("matchId").GetString()!;
        await SendAsync(red, Envelope("HISTORY_DETAIL_REQUEST", "detail", redLogin.Token, null, 7, new { matchId }));
        var detail = await ReadUntilAsync(red, "HISTORY_DETAIL_RESULT");
        Assert.Equal(roomId, detail.GetProperty("payload").GetProperty("match").GetProperty("roomId").GetString());
        Assert.True(challenges.TryGetRoom(roomId, out var endedRoom));
        Assert.True(endedRoom.IsTerminal);
    }

    [Fact]
    public async Task PublicMatch_ResignationEndsForBothPlayersAndPersistsResult()
    {
        using var database = TestDatabase.Create();
        var (server, _, _) = CreateServer(database);
        await using var guard = server;
        await server.StartAsync();
        using var redClient = await ConnectAsync(server);
        using var blackClient = await ConnectAsync(server);
        var red = redClient.GetStream();
        var black = blackClient.GetStream();
        var redLogin = await LoginAsync(red, "Resign-Red");
        var blackLogin = await LoginAsync(black, "Resign-Black");
        var roomId = await CreateAndJoinRoomAsync(red, redLogin.Token, black, blackLogin.Token);

        await SendAsync(red, Envelope("RESIGN_REQUEST", "resign", redLogin.Token, roomId, 4, new { confirmationId = "confirmed" }));
        var redEnded = await ReadUntilAsync(red, "GAME_ENDED");
        var blackEnded = await ReadUntilAsync(black, "GAME_ENDED");
        var result = redEnded.GetProperty("payload").GetProperty("finalResult");
        Assert.Equal("BLACK_WIN", result.GetProperty("resultType").GetString());
        Assert.Equal("RESIGNATION", result.GetProperty("endReason").GetString());
        Assert.Equal("BLACK", blackEnded.GetProperty("payload").GetProperty("finalResult").GetProperty("winnerSide").GetString());
        Assert.Equal("RESIGNATION", database.Service.GetMatch(roomId)!.EndReason);
    }

    [Fact]
    public async Task FinishedMatch_RematchRequiresOpponentAcceptanceAndSwapsColors()
    {
        using var database = TestDatabase.Create();
        var (server, _, challenges) = CreateServer(database);
        await using var guard = server;
        await server.StartAsync();
        using var redClient = await ConnectAsync(server);
        using var blackClient = await ConnectAsync(server);
        var red = redClient.GetStream();
        var black = blackClient.GetStream();
        var redLogin = await LoginAsync(red, "Rematch-Red");
        var blackLogin = await LoginAsync(black, "Rematch-Black");
        var originalRoomId = await CreateAndJoinRoomAsync(red, redLogin.Token, black, blackLogin.Token);

        await SendAsync(red, Envelope("RESIGN_REQUEST", "finish-old", redLogin.Token, originalRoomId, 4,
            new { confirmationId = "confirmed" }));
        await ReadUntilAsync(red, "GAME_ENDED");
        await ReadUntilAsync(black, "GAME_ENDED");

        await SendAsync(red, Envelope("REMATCH_REQUEST", "ask-rematch", redLogin.Token, originalRoomId, 5,
            new { originalRoomId }));
        var redOffer = await ReadUntilAsync(red, "REMATCH_OFFERED");
        var blackOffer = await ReadUntilAsync(black, "REMATCH_OFFERED");
        Assert.Equal(redLogin.PlayerId, redOffer.GetProperty("payload").GetProperty("requestedBy").GetString());
        Assert.Equal(blackLogin.PlayerId, blackOffer.GetProperty("payload").GetProperty("targetPlayerId").GetString());
        Assert.Empty(challenges.GetRoomsSnapshot().Where(room => !room.IsTerminal));

        await SendAsync(red, Envelope("REMATCH_CANCEL", "cancel-rematch", redLogin.Token, originalRoomId, 6,
            new { originalRoomId }));
        await ReadUntilAsync(red, "REMATCH_CANCELLED");
        await ReadUntilAsync(black, "REMATCH_CANCELLED");

        await SendAsync(red, Envelope("REMATCH_REQUEST", "ask-after-cancel", redLogin.Token, originalRoomId, 7,
            new { originalRoomId }));
        await ReadUntilAsync(red, "REMATCH_OFFERED");
        await ReadUntilAsync(black, "REMATCH_OFFERED");
        await SendAsync(black, Envelope("REMATCH_RESPONSE", "decline-rematch", blackLogin.Token, originalRoomId, 5,
            new { originalRoomId, accept = false }));
        await ReadUntilAsync(red, "REMATCH_DECLINED");
        await ReadUntilAsync(black, "REMATCH_DECLINED");
        Assert.Empty(challenges.GetRoomsSnapshot().Where(room => !room.IsTerminal));

        await SendAsync(red, Envelope("REMATCH_REQUEST", "ask-again", redLogin.Token, originalRoomId, 8,
            new { originalRoomId }));
        await ReadUntilAsync(red, "REMATCH_OFFERED");
        await ReadUntilAsync(black, "REMATCH_OFFERED");
        await SendAsync(black, Envelope("REMATCH_RESPONSE", "accept-rematch", blackLogin.Token, originalRoomId, 6,
            new { originalRoomId, accept = true }));
        var redCreated = await ReadUntilAsync(red, "ROOM_CREATED");
        var redSnapshot = await ReadUntilAsync(red, "GAME_STATE_SNAPSHOT");
        var blackCreated = await ReadUntilAsync(black, "ROOM_CREATED");
        var blackSnapshot = await ReadUntilAsync(black, "GAME_STATE_SNAPSHOT");
        var newRoomId = redCreated.GetProperty("payload").GetProperty("roomId").GetString();
        Assert.NotEqual(originalRoomId, newRoomId);
        Assert.Equal(newRoomId, blackCreated.GetProperty("payload").GetProperty("roomId").GetString());
        Assert.Equal("PLAYER_BLACK", redSnapshot.GetProperty("payload").GetProperty("viewerRole").GetString());
        Assert.Equal("PLAYER_RED", blackSnapshot.GetProperty("payload").GetProperty("viewerRole").GetString());
        Assert.Equal(0, redSnapshot.GetProperty("payload").GetProperty("revision").GetInt64());
        Assert.Equal(32, blackSnapshot.GetProperty("payload").GetProperty("pieces").GetArrayLength());
    }

    [Fact]
    public async Task DisconnectedPlayer_ResumesSamePublicMatchWithinReconnectWindow()
    {
        using var database = TestDatabase.Create();
        var (server, players, _) = CreateServer(database);
        await using var guard = server;
        await server.StartAsync();
        using var redClient = await ConnectAsync(server);
        using var blackClient = await ConnectAsync(server);
        var red = redClient.GetStream();
        var black = blackClient.GetStream();
        var redLogin = await LoginAsync(red, "Reconnect-Red");
        var blackLogin = await LoginAsync(black, "Reconnect-Black");
        var roomId = await CreateAndJoinRoomAsync(red, redLogin.Token, black, blackLogin.Token);

        redClient.Dispose();
        for (var attempt = 0; attempt < 50; attempt++)
        {
            if (players.TryGetByPlayerId(redLogin.PlayerId, out var session) &&
                session.ConnectionState == PlayerSessionConnectionState.RECONNECTING) break;
            await Task.Delay(20);
        }
        Assert.True(players.TryGetByPlayerId(redLogin.PlayerId, out var disconnected));
        Assert.Equal(PlayerSessionConnectionState.RECONNECTING, disconnected.ConnectionState);

        using var resumedClient = await ConnectAsync(server);
        var resumed = resumedClient.GetStream();
        await SendAsync(resumed, Envelope("HELLO", "hello-resume", null, null, 1,
            new { protocolVersion = "1.0", clientName = "UDM18.WPF" }));
        await ReadUntilAsync(resumed, "HELLO_ACK");
        await SendAsync(resumed, Envelope("RECONNECT_REQUEST", "resume", redLogin.Token, roomId, 2,
            new { resumeToken = redLogin.Token }));
        var accepted = await ReadUntilAsync(resumed, "RECONNECT_ACCEPTED");
        Assert.Equal(roomId, accepted.GetProperty("payload").GetProperty("roomId").GetString());
        var snapshot = await ReadUntilAsync(resumed, "GAME_STATE_SNAPSHOT");
        Assert.Equal("PLAYER_RED", snapshot.GetProperty("payload").GetProperty("viewerRole").GetString());
        Assert.Equal(PlayerSessionConnectionState.CONNECTED, disconnected.ConnectionState);
    }

    private static (GameServerHost Server, PlayerSessionDirectory Players, ChallengeManager Challenges) CreateServer(TestDatabase database)
    {
        var router = new MessageRouter();
        router.Register("HELLO", HelloMessageHandler.HandleAsync);
        var players = new PlayerSessionDirectory(reconnectWindow: TimeSpan.FromSeconds(60));
        var challenges = new ChallengeManager(players);
        var server = new GameServerHost("127.0.0.1", 0, router, players, challenges);
        LobbyMessageRoutes.Register(router, players, challenges, server);
        MoveMessageRoutes.Register(router, players, challenges, server, database.Service);
        PhaseRoutes.Register(router, players, challenges, server, database.Service);
        return (server, players, challenges);
    }

    private static async Task<TcpClient> ConnectAsync(GameServerHost server)
    {
        var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", server.BoundPort!.Value);
        return client;
    }

    private static async Task<(string PlayerId, string Token)> LoginAsync(NetworkStream stream, string displayName)
    {
        await SendAsync(stream, Envelope("HELLO", $"hello-{displayName}", null, null, 1,
            new { protocolVersion = "1.0", clientName = "UDM18.BusinessQA" }));
        await ReadUntilAsync(stream, "HELLO_ACK");
        await SendAsync(stream, Envelope("LOGIN_REQUEST", $"login-{displayName}", null, null, 2,
            new { displayName, resumeToken = (string?)null }));
        var login = await ReadUntilAsync(stream, "LOGIN_RESULT");
        var payload = login.GetProperty("payload");
        return (payload.GetProperty("player").GetProperty("playerId").GetString()!, payload.GetProperty("token").GetString()!);
    }

    private static async Task<string> CreateAndJoinRoomAsync(
        NetworkStream red, string redToken, NetworkStream black, string blackToken)
    {
        await SendAsync(red, Envelope("WAITING_ROOM_CREATE", "create-room", redToken, null, 3, new { timeProfile = "10+0" }));
        var created = await ReadUntilAsync(red, "WAITING_ROOM_CREATED");
        var roomId = created.GetProperty("payload").GetProperty("roomId").GetString()!;
        await SendAsync(black, Envelope("WAITING_ROOM_JOIN", "join-room", blackToken, roomId, 3, new { roomId }));
        await ReadUntilAsync(red, "ROOM_CREATED");
        await ReadUntilAsync(red, "GAME_STATE_SNAPSHOT");
        await ReadUntilAsync(black, "ROOM_CREATED");
        await ReadUntilAsync(black, "GAME_STATE_SNAPSHOT");
        return roomId;
    }

    private static object Envelope(string type, string requestId, string? token, string? roomId, long sequence, object payload) => new
    {
        protocolVersion = "1.0", type, requestId, sessionToken = token, roomId,
        clientSequence = sequence, sentAtUtc = DateTimeOffset.UtcNow, payload
    };

    private static async Task<JsonElement> ReadUntilAsync(NetworkStream stream, string expectedType)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        while (true)
        {
            var header = new byte[4];
            await stream.ReadExactlyAsync(header, timeout.Token);
            var payload = new byte[BinaryPrimitives.ReadInt32BigEndian(header)];
            await stream.ReadExactlyAsync(payload, timeout.Token);
            using var document = JsonDocument.Parse(payload);
            var frame = document.RootElement.Clone();
            var type = frame.GetProperty("type").GetString();
            if (type == expectedType) return frame;
            if (type == "ERROR_RESPONSE") throw new InvalidOperationException(frame.GetRawText());
        }
    }

    private static async Task SendAsync(NetworkStream stream, object message)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(message, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var header = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, payload.Length);
        await stream.WriteAsync(header);
        await stream.WriteAsync(payload);
        await stream.FlushAsync();
    }
}
