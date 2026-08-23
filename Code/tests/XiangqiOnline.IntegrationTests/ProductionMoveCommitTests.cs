using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text.Json;
using XiangqiOnline.IntegrationTests.Fixtures;
using XiangqiOnline.Server.Lobby;
using XiangqiOnline.Server.Networking;
using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;

namespace XiangqiOnline.IntegrationTests;

public sealed class ProductionMoveCommitTests
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<NetworkStream, string> Tokens = new();
    [Fact]
    public async Task MoveRequest_ValidRedMove_CommitsAndBroadcastsToBothPlayers()
    {
        await using var scenario = await MoveScenario.CreateAsync();
        const string clientMoveId = "move-happy-1";

        await SendMoveAsync(scenario.AliceStream, scenario.AliceId, scenario.Room.RoomId,
            clientMoveId, 0, new Position(0, 9), new Position(0, 7));

        var aliceEvent = await ReadOneFrameAsync(scenario.AliceStream);
        var bobEvent = await ReadOneFrameAsync(scenario.BobStream);
        AssertCommitted(aliceEvent, scenario.Room.RoomId, clientMoveId);
        AssertCommitted(bobEvent, scenario.Room.RoomId, clientMoveId);

        var alicePayload = aliceEvent.GetProperty("payload");
        var bobPayload = bobEvent.GetProperty("payload");
        Assert.Equal(alicePayload.GetProperty("pieceId").GetString(), bobPayload.GetProperty("pieceId").GetString());
        Assert.Equal(alicePayload.GetProperty("from").GetRawText(), bobPayload.GetProperty("from").GetRawText());
        Assert.Equal(alicePayload.GetProperty("to").GetRawText(), bobPayload.GetProperty("to").GetRawText());

        Assert.Equal(1, scenario.Room.Revision);
        Assert.Equal(SideColor.Black, scenario.Room.CurrentTurn);
        Assert.Equal("RED_CHARIOT_1", scenario.Room.Board.GetPieceAt(new Position(0, 7))?.Id);
        Assert.Null(scenario.Room.Board.GetPieceAt(new Position(0, 9)));

        Assert.Equal(1, scenario.Database.Service.CountMoves(scenario.Room.RoomId));
        var persisted = Assert.Single(scenario.Database.Service.ListMoves(scenario.Room.RoomId));
        Assert.Equal(1, persisted.Revision);
        Assert.Equal(clientMoveId, persisted.ClientMoveId);
        Assert.Equal("RED_CHARIOT_1", persisted.PieceId);
        Assert.Equal(new Position(0, 9), persisted.From);
        Assert.Equal(new Position(0, 7), persisted.To);
    }

    [Fact]
    public async Task MoveRequest_StaleExpectedRevision_IsRejectedWithoutMutationOrPersistence()
    {
        await using var scenario = await MoveScenario.CreateAsync();

        await SendMoveAsync(scenario.AliceStream, scenario.AliceId, scenario.Room.RoomId,
            "move-stale-1", 1, new Position(0, 9), new Position(0, 7));

        AssertRejected(await ReadOneFrameAsync(scenario.AliceStream), "REVISION_MISMATCH", 0);
        Assert.Equal(0, scenario.Room.Revision);
        Assert.Equal(0, scenario.Database.Service.CountMoves(scenario.Room.RoomId));
    }

    [Fact]
    public async Task MoveRequest_BlackRequesterDuringRedTurn_IsRejectedByServerAuthorization()
    {
        await using var scenario = await MoveScenario.CreateAsync();

        await SendMoveAsync(scenario.BobStream, scenario.BobId, scenario.Room.RoomId,
            "move-wrong-side-1", 0, new Position(0, 9), new Position(0, 7));

        AssertRejected(await ReadOneFrameAsync(scenario.BobStream), ErrorCodes.NOT_YOUR_TURN, 0);
        Assert.Equal(0, scenario.Room.Revision);
        Assert.Equal(0, scenario.Database.Service.CountMoves(scenario.Room.RoomId));
    }

    [Fact]
    public async Task MoveRequest_InvalidRuleEngineMove_IsRejectedWithoutMutationOrPersistence()
    {
        await using var scenario = await MoveScenario.CreateAsync();

        await SendMoveAsync(scenario.AliceStream, scenario.AliceId, scenario.Room.RoomId,
            "move-invalid-1", 0, new Position(0, 9), new Position(1, 8));

        AssertRejected(await ReadOneFrameAsync(scenario.AliceStream), ErrorCodes.INVALID_GEOMETRY, 0);
        Assert.Equal(0, scenario.Room.Revision);
        Assert.Equal(0, scenario.Database.Service.CountMoves(scenario.Room.RoomId));
    }

    [Fact]
    public async Task MoveRequest_InvalidSessionToken_IsRejectedWithoutMutationOrPersistence()
    {
        await using var scenario = await MoveScenario.CreateAsync();
        var validToken = Tokens[scenario.AliceStream];
        Tokens[scenario.AliceStream] = "invalid-token";
        try
        {
            await SendMoveAsync(scenario.AliceStream, scenario.AliceId, scenario.Room.RoomId,
                "move-invalid-session", 0, new Position(0, 9), new Position(0, 7));
            AssertRejected(await ReadOneFrameAsync(scenario.AliceStream), ErrorCodes.INVALID_SESSION, 0);
            Assert.Equal(0, scenario.Room.Revision);
            Assert.Equal(0, scenario.Database.Service.CountMoves(scenario.Room.RoomId));
        }
        finally
        {
            Tokens[scenario.AliceStream] = validToken;
        }
    }

    private static void AssertCommitted(JsonElement frame, string roomId, string clientMoveId)
    {
        Assert.Equal("MOVE_COMMITTED", frame.GetProperty("type").GetString());
        Assert.Equal(roomId, frame.GetProperty("roomId").GetString());
        Assert.Equal(1, frame.GetProperty("revision").GetInt64());
        var payload = frame.GetProperty("payload");
        Assert.Equal("RED", payload.GetProperty("side").GetString());
        Assert.Equal("RED_CHARIOT_1", payload.GetProperty("pieceId").GetString());
        Assert.Equal(0, payload.GetProperty("from").GetProperty("x").GetInt32());
        Assert.Equal(9, payload.GetProperty("from").GetProperty("y").GetInt32());
        Assert.Equal(0, payload.GetProperty("to").GetProperty("x").GetInt32());
        Assert.Equal(7, payload.GetProperty("to").GetProperty("y").GetInt32());
        Assert.Equal("BLACK", payload.GetProperty("currentTurn").GetString());
        Assert.Equal("move-happy-1", clientMoveId);
    }

    private static void AssertRejected(JsonElement frame, string errorCode, long revision)
    {
        Assert.Equal("MOVE_REJECTED", frame.GetProperty("type").GetString());
        Assert.Equal(revision, frame.GetProperty("revision").GetInt64());
        Assert.Equal(errorCode, frame.GetProperty("payload").GetProperty("errorCode").GetString());
        Assert.Equal(revision, frame.GetProperty("payload").GetProperty("revision").GetInt64());
    }

    private static Task SendMoveAsync(
        NetworkStream stream,
        string playerId,
        string roomId,
        string clientMoveId,
        long expectedRevision,
        Position from,
        Position to)
    {
        return SendAsync(stream, new
        {
            protocolVersion = "1.0",
            type = "MOVE_REQUEST",
            requestId = $"MOVE-{clientMoveId}",
            sessionToken = Tokens[stream],
            roomId,
            clientSequence = 5L,
            sentAtUtc = DateTimeOffset.UtcNow,
            payload = new
            {
                clientMoveId,
                expectedRevision,
                from = new { x = from.X, y = from.Y },
                to = new { x = to.X, y = to.Y }
            }
        });
    }

    private static async Task<string> LoginAsync(NetworkStream stream, string displayName)
    {
        await SendAsync(stream, new
        {
            protocolVersion = "1.0",
            type = "HELLO",
            requestId = $"HELLO-{displayName}",
            sessionToken = (string?)null,
            roomId = (string?)null,
            clientSequence = 1L,
            sentAtUtc = DateTimeOffset.UtcNow,
            payload = new { protocolVersion = "1.0", clientName = "UDM18.WPF" }
        });
        Assert.Equal("HELLO_ACK", (await ReadOneFrameAsync(stream)).GetProperty("type").GetString());

        await SendAsync(stream, new
        {
            protocolVersion = "1.0",
            type = "LOGIN_REQUEST",
            requestId = $"LOGIN-{displayName}",
            sessionToken = (string?)null,
            roomId = (string?)null,
            clientSequence = 2L,
            sentAtUtc = DateTimeOffset.UtcNow,
            payload = new { displayName, resumeToken = (string?)null }
        });
        var login = await ReadOneFrameAsync(stream);
        Assert.Equal("LOGIN_RESULT", login.GetProperty("type").GetString());
        Tokens[stream] = login.GetProperty("payload").GetProperty("token").GetString()!;
        return login.GetProperty("payload").GetProperty("player").GetProperty("playerId").GetString()!;
    }

    private static async Task<JsonElement> ReadOneFrameAsync(NetworkStream stream)
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
            if (count == 0)
                throw new IOException("Server closed the connection.");
            read += count;
        }
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

    private sealed class MoveScenario : IAsyncDisposable
    {
        private MoveScenario(
            TestDatabase database,
            GameServerHost server,
            TcpClient alice,
            TcpClient bob,
            string aliceId,
            string bobId,
            GameRoom room)
        {
            Database = database;
            Server = server;
            Alice = alice;
            Bob = bob;
            AliceId = aliceId;
            BobId = bobId;
            Room = room;
        }

        public TestDatabase Database { get; }
        public GameServerHost Server { get; }
        public TcpClient Alice { get; }
        public TcpClient Bob { get; }
        public NetworkStream AliceStream => Alice.GetStream();
        public NetworkStream BobStream => Bob.GetStream();
        public string AliceId { get; }
        public string BobId { get; }
        public GameRoom Room { get; }

        public static async Task<MoveScenario> CreateAsync()
        {
            var database = TestDatabase.Create();
            var router = new MessageRouter();
            router.Register("HELLO", HelloMessageHandler.HandleAsync);
            var players = new PlayerSessionDirectory();
            var challenges = new ChallengeManager(players);
            var server = new GameServerHost("127.0.0.1", 0, router, players);
            LobbyMessageRoutes.Register(router, players, challenges, server);
            MoveMessageRoutes.Register(router, players, challenges, server, database.Service);
            await server.StartAsync();

            var alice = new TcpClient();
            var bob = new TcpClient();
            await Task.WhenAll(
                alice.ConnectAsync("127.0.0.1", server.BoundPort!.Value),
                bob.ConnectAsync("127.0.0.1", server.BoundPort.Value));
            var aliceId = await LoginAsync(alice.GetStream(), "Alice");
            var bobId = await LoginAsync(bob.GetStream(), "Bob");

            await SendAsync(alice.GetStream(), new
            {
                protocolVersion = "1.0",
                type = "CHALLENGE_SEND",
                requestId = "CHALLENGE-SEND",
                sessionToken = Tokens[alice.GetStream()],
                roomId = (string?)null,
                clientSequence = 3L,
                sentAtUtc = DateTimeOffset.UtcNow,
                payload = new { targetPlayerId = bobId, timeProfile = "STANDARD_PRO" }
            });
            var invitation = await ReadOneFrameAsync(bob.GetStream());
            Assert.Equal("CHALLENGE_RECEIVED", invitation.GetProperty("type").GetString());
            var sent = await ReadOneFrameAsync(alice.GetStream());
            Assert.Equal("CHALLENGE_SENT", sent.GetProperty("type").GetString());
            var challengeId = invitation.GetProperty("payload").GetProperty("challenge")
                .GetProperty("challengeId").GetString()!;

            await SendAsync(bob.GetStream(), new
            {
                protocolVersion = "1.0",
                type = "CHALLENGE_ACCEPT",
                requestId = "CHALLENGE-ACCEPT",
                sessionToken = Tokens[bob.GetStream()],
                roomId = (string?)null,
                clientSequence = 4L,
                sentAtUtc = DateTimeOffset.UtcNow,
                payload = new { challengeId }
            });

            var aliceRoom = await ReadOneFrameAsync(alice.GetStream());
            var aliceSnapshot = await ReadOneFrameAsync(alice.GetStream());
            var bobRoom = await ReadOneFrameAsync(bob.GetStream());
            var bobSnapshot = await ReadOneFrameAsync(bob.GetStream());
            Assert.Equal("ROOM_CREATED", aliceRoom.GetProperty("type").GetString());
            Assert.Equal("GAME_STATE_SNAPSHOT", aliceSnapshot.GetProperty("type").GetString());
            Assert.Equal("ROOM_CREATED", bobRoom.GetProperty("type").GetString());
            Assert.Equal("GAME_STATE_SNAPSHOT", bobSnapshot.GetProperty("type").GetString());

            var roomId = aliceRoom.GetProperty("payload").GetProperty("roomId").GetString()!;
            Assert.True(challenges.TryGetRoom(roomId, out var room));
            Assert.NotNull(room);
            Assert.Equal(aliceId, room!.RedPlayerId);
            Assert.Equal(bobId, room.BlackPlayerId);
            Assert.Equal(0, room.Revision);
            return new MoveScenario(database, server, alice, bob, aliceId, bobId, room);
        }

        public async ValueTask DisposeAsync()
        {
            Alice.Dispose();
            Bob.Dispose();
            await Server.DisposeAsync();
            Database.Dispose();
        }
    }
}
