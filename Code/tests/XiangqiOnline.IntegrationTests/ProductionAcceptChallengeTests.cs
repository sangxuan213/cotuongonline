using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;
using XiangqiOnline.Server.Lobby;
using XiangqiOnline.Server.Networking;
using XiangqiOnline.Shared.Models;
using XiangqiOnline.Shared.Protocol;
using XiangqiOnline.Shared.Transport;
using Xunit;

namespace XiangqiOnline.IntegrationTests;

/// <summary>
/// REAL CHALLENGE_ACCEPT end-to-end over the shipped production path:
/// CHALLENGE_SEND -> CHALLENGE_RECEIVED -> CHALLENGE_ACCEPT ->
/// ChallengeManager.AcceptChallenge -> ROOM_CREATED + GAME_STATE_SNAPSHOT sent
/// to BOTH players. Wire format identical to TV5's GameClient.
/// </summary>
public class ProductionAcceptChallengeTests
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<NetworkStream, string> Tokens = new();
    [Fact]
    public async Task AcceptChallenge_BothPlayers_ReceiveRoomCreatedAndSnapshot()
    {
        var (server, players, challenges) = CreateServer();
        await using var serverGuard = server;
        await server.StartAsync();

        using var clientA = new TcpClient();
        using var clientB = new TcpClient();
        await Task.WhenAll(
            clientA.ConnectAsync("127.0.0.1", server.BoundPort!.Value),
            clientB.ConnectAsync("127.0.0.1", server.BoundPort!.Value));
        var streamA = clientA.GetStream();
        var streamB = clientB.GetStream();

        var aliceId = await LoginAsync(streamA, "Alice");
        var bobId = await LoginAsync(streamB, "Bob");
        var alice = players.TryGetByPlayerId(aliceId, out var a) ? a! : throw new Exception("Alice missing.");
        var bob = players.TryGetByPlayerId(bobId, out var b) ? b! : throw new Exception("Bob missing.");

        await SendChallengeAsync(streamA, alice.PlayerId, bob.PlayerId);

        var invitation = await ReadOneFrameAsync(streamB);
        var sent = await ReadOneFrameAsync(streamA);
        Assert.NotNull(invitation);
        Assert.Equal("CHALLENGE_SENT", sent?.GetProperty("type").GetString());
        var challengeId = JsonDocument.Parse(invitation!.Value.GetRawText())
            .RootElement.GetProperty("payload").GetProperty("challenge").GetProperty("challengeId").GetString()!;

        await SendAcceptAsync(streamB, bob.PlayerId, challengeId);

        var aRoom = await ReadOneFrameAsync(streamA);
        var aSnapshot = await ReadOneFrameAsync(streamA);
        var bRoom = await ReadOneFrameAsync(streamB);
        var bSnapshot = await ReadOneFrameAsync(streamB);

        AssertRoomCreated(aRoom);
        AssertSnapshot(aSnapshot);
        AssertRoomCreated(bRoom);
        AssertSnapshot(bSnapshot);

        var roomIdA = JsonDocument.Parse(aRoom!.Value.GetRawText()).RootElement.GetProperty("payload").GetProperty("roomId").GetString();
        var roomIdB = JsonDocument.Parse(bRoom!.Value.GetRawText()).RootElement.GetProperty("payload").GetProperty("roomId").GetString();
        Assert.Equal(roomIdA, roomIdB);
        Assert.True(challenges.TryGetRoom(roomIdA!, out _));

        var snapshotA = ParseSnapshot(aSnapshot!.Value);
        Assert.Equal(roomIdA, snapshotA.RoomId);
        Assert.Equal(32, snapshotA.Pieces.Count);
        Assert.Equal("RED", snapshotA.CurrentTurn);
        Assert.Equal(0L, snapshotA.Revision);
        Assert.Equal("PLAYING", snapshotA.Status);
        Assert.Contains(snapshotA.Pieces, p => p.PieceId == "RED_GENERAL");
        Assert.Contains(snapshotA.Pieces, p => p.PieceId == "BLACK_GENERAL");

        var snapshotB = ParseSnapshot(bSnapshot!.Value);
        Assert.Equal(snapshotA.RoomId, snapshotB.RoomId);
        Assert.Equal(snapshotA.Revision, snapshotB.Revision);
        Assert.Equal(snapshotA.Pieces.Count, snapshotB.Pieces.Count);
        Assert.Equal(PlayerStatus.IN_GAME, alice.Status);
        Assert.Equal(PlayerStatus.IN_GAME, bob.Status);

        await SendQuickChatAsync(streamA, roomIdA!, "GOOD_MOVE", 5);
        var aChat = await ReadOneFrameAsync(streamA);
        var bChat = await ReadOneFrameAsync(streamB);
        AssertQuickChat(aChat, roomIdA!, alice.PlayerId, "Nước hay!");
        AssertQuickChat(bChat, roomIdA!, alice.PlayerId, "Nước hay!");

        await SendQuickChatAsync(streamA, roomIdA!, "SMILE", 6);
        var rateLimited = await ReadOneFrameAsync(streamA);
        Assert.Equal("ERROR_RESPONSE", rateLimited?.GetProperty("type").GetString());
        Assert.Equal(ErrorCodes.RATE_LIMITED, rateLimited?.GetProperty("payload").GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task AcceptChallenge_UnknownChallengeId_ReturnsErrorResponse()
    {
        var (server, players, _) = CreateServer();
        await using var serverGuard = server;
        await server.StartAsync();

        using var clientB = new TcpClient();
        await clientB.ConnectAsync("127.0.0.1", server.BoundPort!.Value);
        var streamB = clientB.GetStream();

        var bobId = await LoginAsync(streamB, "Bob");

        await SendAcceptAsync(streamB, bobId, "missing-challenge");

        var error = await ReadOneFrameAsync(streamB);
        Assert.NotNull(error);
        using (var doc = JsonDocument.Parse(error!.Value.GetRawText()))
        {
            Assert.Equal("ERROR_RESPONSE", doc.RootElement.GetProperty("type").GetString());
            Assert.Equal(ErrorCodes.CHALLENGE_NOT_FOUND, doc.RootElement.GetProperty("payload").GetProperty("errorCode").GetString());
        }
    }

    [Fact]
    public async Task AcceptChallenge_UnrelatedPlayer_ReturnsErrorResponse()
    {
        var (server, players, _) = CreateServer();
        await using var serverGuard = server;
        await server.StartAsync();

        using var clientA = new TcpClient();
        using var clientB = new TcpClient();
        using var clientC = new TcpClient();
        await Task.WhenAll(
            clientA.ConnectAsync("127.0.0.1", server.BoundPort!.Value),
            clientB.ConnectAsync("127.0.0.1", server.BoundPort!.Value),
            clientC.ConnectAsync("127.0.0.1", server.BoundPort!.Value));
        var streamA = clientA.GetStream();
        var streamB = clientB.GetStream();
        var streamC = clientC.GetStream();

        var aliceId = await LoginAsync(streamA, "Alice");
        var bobId = await LoginAsync(streamB, "Bob");
        var charlieId = await LoginAsync(streamC, "Charlie");
        var alice = players.TryGetByPlayerId(aliceId, out var a) ? a! : throw new Exception("Alice missing.");
        var bob = players.TryGetByPlayerId(bobId, out var b) ? b! : throw new Exception("Bob missing.");

        await SendChallengeAsync(streamA, alice.PlayerId, bob.PlayerId);

        var invitation = await ReadOneFrameAsync(streamB);
        Assert.NotNull(invitation);
        var challengeId = JsonDocument.Parse(invitation!.Value.GetRawText())
            .RootElement.GetProperty("payload").GetProperty("challenge").GetProperty("challengeId").GetString()!;

        await SendAcceptAsync(streamC, charlieId, challengeId);

        var error = await ReadOneFrameAsync(streamC);
        Assert.NotNull(error);
        using (var doc = JsonDocument.Parse(error!.Value.GetRawText()))
        {
            Assert.Equal("ERROR_RESPONSE", doc.RootElement.GetProperty("type").GetString());
            Assert.Equal(ErrorCodes.CHALLENGE_UNAUTHORIZED, doc.RootElement.GetProperty("payload").GetProperty("errorCode").GetString());
        }
    }

    private static void AssertRoomCreated(JsonElement? frame)
    {
        Assert.NotNull(frame);
        using (var doc = JsonDocument.Parse(frame!.Value.GetRawText()))
        {
            Assert.Equal("ROOM_CREATED", doc.RootElement.GetProperty("type").GetString());
            Assert.False(string.IsNullOrWhiteSpace(doc.RootElement.GetProperty("payload").GetProperty("roomId").GetString()));
        }
    }

    private static (string RoomId, long Revision, string CurrentTurn, string Status, IReadOnlyList<(string PieceId, string Side, string Type, int X, int Y)> Pieces) ParseSnapshot(JsonElement frame)
    {
        using var doc = JsonDocument.Parse(frame.GetRawText());
        var payload = doc.RootElement.GetProperty("payload");
        var pieces = payload.GetProperty("pieces").EnumerateArray()
            .Select(p => (
                PieceId: p.GetProperty("pieceId").GetString()!,
                Side: p.GetProperty("side").GetString()!,
                Type: p.GetProperty("type").GetString()!,
                X: p.GetProperty("x").GetInt32(),
                Y: p.GetProperty("y").GetInt32()))
            .ToArray();
        return (
            payload.GetProperty("roomId").GetString()!,
            payload.GetProperty("revision").GetInt64(),
            payload.GetProperty("currentTurn").GetString()!,
            payload.GetProperty("status").GetString()!,
            pieces);
    }

    private static void AssertSnapshot(JsonElement? frame)
    {
        Assert.NotNull(frame);
        var snapshot = ParseSnapshot(frame!.Value);
        Assert.False(string.IsNullOrWhiteSpace(snapshot.RoomId));
        Assert.Equal("RED", snapshot.CurrentTurn);
        Assert.Equal(32, snapshot.Pieces.Count);
        Assert.Equal(0L, snapshot.Revision);
    }

    private static void AssertQuickChat(JsonElement? frame, string roomId, string senderPlayerId, string text)
    {
        Assert.NotNull(frame);
        Assert.Equal("QUICK_CHAT_RECEIVED", frame?.GetProperty("type").GetString());
        var payload = frame!.Value.GetProperty("payload");
        Assert.Equal(roomId, payload.GetProperty("roomId").GetString());
        Assert.Equal(senderPlayerId, payload.GetProperty("senderPlayerId").GetString());
        Assert.Equal(text, payload.GetProperty("text").GetString());
    }

    private static async Task<string> LoginAsync(NetworkStream stream, string displayName)
    {
        var hello = new
        {
            protocolVersion = "1.0",
            type = "HELLO",
            requestId = $"01J{displayName}HELLO",
            sessionToken = (string?)null,
            roomId = (string?)null,
            clientSequence = 1L,
            sentAtUtc = DateTimeOffset.UtcNow,
            payload = new { protocolVersion = "1.0", clientName = "UDM18.WPF" }
        };
        await SendAsync(stream, hello);
        var ack = await ReadOneFrameAsync(stream);
        Assert.NotNull(ack);

        var login = new
        {
            protocolVersion = "1.0",
            type = "LOGIN_REQUEST",
            requestId = $"01J{displayName}LOGIN",
            sessionToken = (string?)null,
            roomId = (string?)null,
            clientSequence = 2L,
            sentAtUtc = DateTimeOffset.UtcNow,
            payload = new { displayName, resumeToken = (string?)null }
        };
        await SendAsync(stream, login);
        var loginResult = await ReadOneFrameAsync(stream);
        Assert.NotNull(loginResult);
        using var document = JsonDocument.Parse(loginResult!.Value.GetRawText());
        Tokens[stream] = document.RootElement.GetProperty("payload").GetProperty("token").GetString()!;
        return document.RootElement.GetProperty("payload").GetProperty("player").GetProperty("playerId").GetString()!;
    }

    private static async Task SendChallengeAsync(NetworkStream stream, string challengerPlayerId, string targetPlayerId)
    {
        var challenge = new
        {
            protocolVersion = "1.0",
            type = "CHALLENGE_SEND",
            requestId = $"01J{challengerPlayerId}CHAL",
            sessionToken = Tokens[stream],
            roomId = (string?)null,
            clientSequence = 3L,
            sentAtUtc = DateTimeOffset.UtcNow,
            payload = new { targetPlayerId, timeProfile = "STANDARD_PRO" }
        };
        await SendAsync(stream, challenge);
    }

    private static async Task SendAcceptAsync(NetworkStream stream, string acceptingPlayerId, string challengeId)
    {
        var accept = new
        {
            protocolVersion = "1.0",
            type = "CHALLENGE_ACCEPT",
            requestId = $"01J{acceptingPlayerId}ACPT",
            sessionToken = Tokens[stream],
            roomId = challengeId,
            clientSequence = 4L,
            sentAtUtc = DateTimeOffset.UtcNow,
            payload = new { challengeId }
        };
        await SendAsync(stream, accept);
    }

    private static Task SendQuickChatAsync(NetworkStream stream, string roomId, string code, long sequence) =>
        SendAsync(stream, new
        {
            protocolVersion = "1.0",
            type = "QUICK_CHAT_SEND",
            requestId = $"CHAT-{sequence}",
            sessionToken = Tokens[stream],
            roomId,
            clientSequence = sequence,
            sentAtUtc = DateTimeOffset.UtcNow,
            payload = new { code }
        });

    private static async Task SendAsync(NetworkStream stream, object envelope)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(envelope, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var header = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, payload.Length);
        await stream.WriteAsync(header);
        await stream.WriteAsync(payload);
        await stream.FlushAsync();
    }

    private static async Task<JsonElement?> ReadOneFrameAsync(NetworkStream stream)
    {
        var header = new byte[4];
        await ReadExactlyAsync(stream, header);
        var length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length is <= 0 or > 65_536)
            throw new InvalidDataException($"INVALID_FRAME_LENGTH: {length}");
        var payload = new byte[length];
        await ReadExactlyAsync(stream, payload);
        using var document = JsonDocument.Parse(payload);
        return document.RootElement.Clone();
    }

    private static async Task ReadExactlyAsync(NetworkStream stream, Memory<byte> target)
    {
        var read = 0;
        while (read < target.Length)
        {
            var count = await stream.ReadAsync(target[read..]);
            if (count == 0) throw new IOException("Server đã đóng kết nối.");
            read += count;
        }
    }

    private static (GameServerHost Server, PlayerSessionDirectory Players, ChallengeManager Challenges) CreateServer()
    {
        var router = new MessageRouter();
        router.Register("HELLO", HelloMessageHandler.HandleAsync);
        var players = new PlayerSessionDirectory();
        var challenges = new ChallengeManager(players);
        var server = new GameServerHost("127.0.0.1", 0, router, players);
        LobbyMessageRoutes.Register(router, players, challenges, server);
        return (server, players, challenges);
    }
}
