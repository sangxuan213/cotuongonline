using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text.Json;
using XiangqiOnline.IntegrationTests.Fixtures;
using XiangqiOnline.Server.Lobby;
using XiangqiOnline.Server.Networking;

namespace XiangqiOnline.IntegrationTests;

public sealed class ProductionBotGameTests
{
    [Theory]
    [InlineData("EASY")]
    [InlineData("MEDIUM")]
    [InlineData("HARD")]
    public async Task RealServer_BotGame_AcceptsHumanMoveAndRepliesWithLegalBotMove(string difficulty)
    {
        using var database = TestDatabase.Create();
        var router = new MessageRouter();
        router.Register("HELLO", HelloMessageHandler.HandleAsync);
        var players = new PlayerSessionDirectory();
        var challenges = new ChallengeManager(players, roomIdFactory: () => $"bot-{difficulty.ToLowerInvariant()}");
        await using var server = new GameServerHost("127.0.0.1", 0, router, players);
        LobbyMessageRoutes.Register(router, players, challenges, server);
        var bots = new BotMoveService(players, server, database.Service);
        MoveMessageRoutes.Register(router, players, challenges, server, database.Service, bots);
        PhaseRoutes.Register(router, players, challenges, server, database.Service, bots);
        await server.StartAsync();

        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", server.BoundPort!.Value);
        var stream = client.GetStream();
        var token = await LoginAsync(stream);

        await SendAsync(stream, Envelope("BOT_GAME_REQUEST", "bot-request", token, null, 3,
            new { difficulty }));
        var roomCreated = await ReadUntilAsync(stream, "ROOM_CREATED");
        var roomId = roomCreated.GetProperty("payload").GetProperty("roomId").GetString()!;
        var snapshot = await ReadUntilAsync(stream, "GAME_STATE_SNAPSHOT");
        Assert.Equal("PLAYER_RED", snapshot.GetProperty("payload").GetProperty("viewerRole").GetString());
        Assert.Equal(JsonValueKind.String,
            snapshot.GetProperty("payload").GetProperty("clocks").GetProperty("activeSide").ValueKind);
        Assert.Equal("RED", snapshot.GetProperty("payload").GetProperty("clocks").GetProperty("activeSide").GetString());
        Assert.Equal(0, snapshot.GetProperty("payload").GetProperty("clocks").GetProperty("incrementMs").GetInt64());

        await SendAsync(stream, Envelope("MOVE_REQUEST", "human-move", token, roomId, 4, new
        {
            clientMoveId = "human-move-1",
            expectedRevision = 0,
            from = new { x = 0, y = 9 },
            to = new { x = 0, y = 7 }
        }));

        var humanMove = await ReadUntilAsync(stream, "MOVE_COMMITTED");
        Assert.Equal(1, humanMove.GetProperty("revision").GetInt64());
        Assert.Equal("RED", humanMove.GetProperty("payload").GetProperty("side").GetString());
        var humanClocks = humanMove.GetProperty("payload").GetProperty("clocks");
        Assert.InRange(humanClocks.GetProperty("redRemainingMs").GetInt64(), 1, 600_000);
        Assert.Equal(600_000, humanClocks.GetProperty("blackRemainingMs").GetInt64());
        Assert.Equal(0, humanClocks.GetProperty("incrementMs").GetInt64());

        var botMove = await ReadUntilAsync(stream, "MOVE_COMMITTED", TimeSpan.FromSeconds(8));
        Assert.Equal(2, botMove.GetProperty("revision").GetInt64());
        Assert.Equal("BLACK", botMove.GetProperty("payload").GetProperty("side").GetString());
        Assert.Equal("RED", botMove.GetProperty("payload").GetProperty("currentTurn").GetString());
        Assert.Equal(2, database.Service.CountMoves(roomId));
    }

    private static async Task<string> LoginAsync(NetworkStream stream)
    {
        await SendAsync(stream, Envelope("HELLO", "hello", null, null, 1,
            new { protocolVersion = "1.0", clientName = "UDM18.WPF" }));
        Assert.Equal("HELLO_ACK", (await ReadUntilAsync(stream, "HELLO_ACK")).GetProperty("type").GetString());
        await SendAsync(stream, Envelope("LOGIN_REQUEST", "login", null, null, 2,
            new { displayName = $"Bot-{Guid.NewGuid():N}"[..20], resumeToken = (string?)null }));
        var login = await ReadUntilAsync(stream, "LOGIN_RESULT");
        return login.GetProperty("payload").GetProperty("token").GetString()!;
    }

    private static object Envelope(string type, string requestId, string? token, string? roomId, long sequence, object payload) => new
    {
        protocolVersion = "1.0",
        type,
        requestId,
        sessionToken = token,
        roomId,
        clientSequence = sequence,
        sentAtUtc = DateTimeOffset.UtcNow,
        payload
    };

    private static async Task<JsonElement> ReadUntilAsync(NetworkStream stream, string type, TimeSpan? timeout = null)
    {
        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(5));
        while (true)
        {
            var frame = await ReadOneFrameAsync(stream, cts.Token);
            if (frame.GetProperty("type").GetString() == type) return frame;
            if (frame.GetProperty("type").GetString() is "ERROR_RESPONSE" or "MOVE_REJECTED")
                throw new InvalidOperationException(frame.GetRawText());
        }
    }

    private static async Task<JsonElement> ReadOneFrameAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, cancellationToken);
        var length = BinaryPrimitives.ReadInt32BigEndian(header);
        var payload = new byte[length];
        await stream.ReadExactlyAsync(payload, cancellationToken);
        using var document = JsonDocument.Parse(payload);
        return document.RootElement.Clone();
    }

    private static async Task SendAsync(NetworkStream stream, object envelope)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(envelope, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var header = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, payload.Length);
        await stream.WriteAsync(header);
        await stream.WriteAsync(payload);
        await stream.FlushAsync();
    }
}
