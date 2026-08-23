using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text.Json;
using XiangqiOnline.Server.Lobby;
using XiangqiOnline.Server.Networking;

namespace XiangqiOnline.IntegrationTests;

public sealed class PublicWaitingRoomTests
{
    [Fact]
    public async Task RealServer_OwnerCanCancelWaitingRoomAndBecomeAvailableAgain()
    {
        var router = new MessageRouter();
        router.Register("HELLO", HelloMessageHandler.HandleAsync);
        var players = new PlayerSessionDirectory(() => "owner-id");
        var challenges = new ChallengeManager(players, roomIdFactory: () => "cancel-room-1");
        await using var server = new GameServerHost("127.0.0.1", 0, router, players);
        LobbyMessageRoutes.Register(router, players, challenges, server);
        await server.StartAsync();

        using var ownerClient = new TcpClient();
        await ownerClient.ConnectAsync("127.0.0.1", server.BoundPort!.Value);
        var owner = ownerClient.GetStream();
        var ownerToken = await LoginAsync(owner, "Chủ phòng");

        await SendAsync(owner, Envelope("WAITING_ROOM_CREATE", "create", ownerToken, null, 3, new { timeProfile = "10+0" }));
        await ReadUntilAsync(owner, "WAITING_ROOM_CREATED");
        await SendAsync(owner, Envelope("WAITING_ROOM_CANCEL", "cancel", ownerToken, "cancel-room-1", 4, new { roomId = "cancel-room-1" }));
        var cancelled = await ReadUntilAsync(owner, "WAITING_ROOM_CANCELLED");

        Assert.Equal("cancel-room-1", cancelled.GetProperty("payload").GetProperty("roomId").GetString());
        Assert.Empty(challenges.GetWaitingRoomsSnapshot());
        Assert.Equal(PlayerStatus.AVAILABLE, players.GetSnapshot().Single().Status);
    }

    [Fact]
    public async Task RealServer_TwoClients_CreateAndJoinPublicRoom()
    {
        var router = new MessageRouter();
        router.Register("HELLO", HelloMessageHandler.HandleAsync);
        var ids = new Queue<string>(["owner-id", "guest-id"]);
        var players = new PlayerSessionDirectory(() => ids.Dequeue());
        var challenges = new ChallengeManager(players, roomIdFactory: () => "public-room-1");
        await using var server = new GameServerHost("127.0.0.1", 0, router, players);
        LobbyMessageRoutes.Register(router, players, challenges, server);
        await server.StartAsync();

        using var ownerClient = new TcpClient();
        using var guestClient = new TcpClient();
        await ownerClient.ConnectAsync("127.0.0.1", server.BoundPort!.Value);
        await guestClient.ConnectAsync("127.0.0.1", server.BoundPort.Value);
        var owner = ownerClient.GetStream();
        var guest = guestClient.GetStream();
        var ownerToken = await LoginAsync(owner, "Chủ phòng");
        var guestToken = await LoginAsync(guest, "Khách");

        await SendAsync(owner, Envelope("WAITING_ROOM_CREATE", "create", ownerToken, null, 3, new { timeProfile = "10+0" }));
        var created = await ReadUntilAsync(owner, "WAITING_ROOM_CREATED");
        Assert.Equal("public-room-1", created.GetProperty("payload").GetProperty("roomId").GetString());

        var waitingList = await ReadUntilAsync(guest, "WAITING_ROOMS_UPDATED");
        var listed = waitingList.GetProperty("payload").GetProperty("rooms").EnumerateArray().Single();
        Assert.Equal("Chủ phòng", listed.GetProperty("ownerDisplayName").GetString());

        await SendAsync(guest, Envelope("WAITING_ROOM_JOIN", "join", guestToken, "public-room-1", 3, new { roomId = "public-room-1" }));
        var ownerRoom = await ReadUntilAsync(owner, "ROOM_CREATED");
        var guestRoom = await ReadUntilAsync(guest, "ROOM_CREATED");
        Assert.Equal("public-room-1", ownerRoom.GetProperty("payload").GetProperty("roomId").GetString());
        Assert.Equal("public-room-1", guestRoom.GetProperty("payload").GetProperty("roomId").GetString());

        var ownerSnapshot = await ReadUntilAsync(owner, "GAME_STATE_SNAPSHOT");
        var guestSnapshot = await ReadUntilAsync(guest, "GAME_STATE_SNAPSHOT");
        Assert.Equal("PLAYER_RED", ownerSnapshot.GetProperty("payload").GetProperty("viewerRole").GetString());
        Assert.Equal("PLAYER_BLACK", guestSnapshot.GetProperty("payload").GetProperty("viewerRole").GetString());
        Assert.Equal(0, ownerSnapshot.GetProperty("payload").GetProperty("clocks").GetProperty("incrementMs").GetInt64());
        Assert.Empty(challenges.GetWaitingRoomsSnapshot());
        Assert.All(players.GetSnapshot(), player => Assert.Equal(PlayerStatus.IN_GAME, player.Status));
    }

    private static async Task<string> LoginAsync(NetworkStream stream, string displayName)
    {
        await SendAsync(stream, Envelope("HELLO", "hello", null, null, 1, new { protocolVersion = "1.0", clientName = "UDM18.WPF" }));
        await ReadUntilAsync(stream, "HELLO_ACK");
        await SendAsync(stream, Envelope("LOGIN_REQUEST", "login", null, null, 2, new { displayName, resumeToken = (string?)null }));
        var login = await ReadUntilAsync(stream, "LOGIN_RESULT");
        return login.GetProperty("payload").GetProperty("token").GetString()!;
    }

    private static object Envelope(string type, string requestId, string? token, string? roomId, long sequence, object payload) => new
    {
        protocolVersion = "1.0", type, requestId, sessionToken = token, roomId,
        clientSequence = sequence, sentAtUtc = DateTimeOffset.UtcNow, payload
    };

    private static async Task<JsonElement> ReadUntilAsync(NetworkStream stream, string type)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            var frame = await ReadOneFrameAsync(stream, cts.Token);
            var actual = frame.GetProperty("type").GetString();
            if (actual == type) return frame;
            if (actual == "ERROR_RESPONSE") throw new InvalidOperationException(frame.GetRawText());
        }
    }

    private static async Task<JsonElement> ReadOneFrameAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, cancellationToken);
        var payload = new byte[BinaryPrimitives.ReadInt32BigEndian(header)];
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
