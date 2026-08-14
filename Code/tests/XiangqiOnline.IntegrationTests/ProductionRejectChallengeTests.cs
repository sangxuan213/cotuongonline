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

namespace XiangqiOnline.IntegrationTests
{
    /// <summary>
    /// REAL CHALLENGE_SEND -> CHALLENGE_RECEIVED -> CHALLENGE_REJECT end-to-end over
    /// the shipped production path, followed by CHALLENGE_REJECTED sent to BOTH the
    /// challenger and the target. Reject never creates a GameRoom, so no ROOM_CREATED
    /// or GAME_STATE_SNAPSHOT events are emitted. Wire format is identical to TV5's
    /// GameClient.
    /// </summary>
    public class ProductionRejectChallengeTests
    {
        [Fact]
        public async Task RejectChallenge_BothPlayers_ReceiveRejectedEventAndReturnToAvailable()
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
            Assert.NotNull(invitation);
            var challengeId = invitation!.Value.GetProperty("payload").GetProperty("challenge")
                .GetProperty("challengeId").GetString()!;

            await SendRejectAsync(streamB, bob.PlayerId, challengeId);

            var aRejected = await ReadOneFrameAsync(streamA);
            var bRejected = await ReadOneFrameAsync(streamB);

            AssertRejected(aRejected, challengeId, bob.PlayerId);
            AssertRejected(bRejected, challengeId, bob.PlayerId);

            Assert.Equal(PlayerStatus.AVAILABLE, alice.Status);
            Assert.Null(alice.ActiveChallengeId);
            Assert.Equal(PlayerStatus.AVAILABLE, bob.Status);
            Assert.Null(bob.ActiveChallengeId);

            Assert.True(challenges.TryGetChallenge(challengeId, out var challenge));
            Assert.Equal(ChallengeStatus.REJECTED, challenge!.Status);
        }

        [Fact]
        public async Task RejectChallenge_UnknownChallengeId_ReturnsErrorResponse()
        {
            var (server, players, _) = CreateServer();
            await using var serverGuard = server;
            await server.StartAsync();

            using var clientB = new TcpClient();
            await clientB.ConnectAsync("127.0.0.1", server.BoundPort!.Value);
            var streamB = clientB.GetStream();

            var bobId = await LoginAsync(streamB, "Bob");

            await SendRejectAsync(streamB, bobId, "missing-challenge");

            var error = await ReadOneFrameAsync(streamB);
            Assert.NotNull(error);
            using (var doc = JsonDocument.Parse(error!.Value.GetRawText()))
            {
                Assert.Equal("ERROR_RESPONSE", doc.RootElement.GetProperty("type").GetString());
                Assert.Equal(ErrorCodes.CHALLENGE_NOT_FOUND, doc.RootElement.GetProperty("payload").GetProperty("errorCode").GetString());
            }
        }

        [Fact]
        public async Task RejectChallenge_UnrelatedPlayer_ReturnsErrorResponse()
        {
            var (server, players, challenges) = CreateServer();
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

            var challengeId = SeedPendingChallenge(challenges, alice.PlayerId, bob.PlayerId);

            await SendRejectAsync(streamC, charlieId, challengeId);

            var error = await ReadOneFrameAsync(streamC);
            Assert.NotNull(error);
            using (var doc = JsonDocument.Parse(error!.Value.GetRawText()))
            {
                Assert.Equal("ERROR_RESPONSE", doc.RootElement.GetProperty("type").GetString());
                Assert.Equal(ErrorCodes.CHALLENGE_UNAUTHORIZED, doc.RootElement.GetProperty("payload").GetProperty("errorCode").GetString());
            }
        }

        [Fact]
        public async Task RejectChallenge_AfterAccept_ReturnsNotPendingError()
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

            var challengeId = SeedPendingChallenge(challenges, alice.PlayerId, bob.PlayerId);

            var accept = challenges.AcceptChallenge(challengeId, bob.PlayerId, DateTimeOffset.UtcNow);
            Assert.True(accept.IsSuccess, $"Seeding accept should succeed: {accept.Message}");

            await SendRejectAsync(streamB, bob.PlayerId, challengeId);

            var error = await ReadOneFrameAsync(streamB);
            Assert.NotNull(error);
            using (var doc = JsonDocument.Parse(error!.Value.GetRawText()))
            {
                Assert.Equal("ERROR_RESPONSE", doc.RootElement.GetProperty("type").GetString());
                Assert.Equal(ErrorCodes.CHALLENGE_NOT_PENDING, doc.RootElement.GetProperty("payload").GetProperty("errorCode").GetString());
            }
        }

        private static void AssertRejected(JsonElement? frame, string challengeId, string rejectedByPlayerId)
        {
            Assert.NotNull(frame);
            using (var doc = JsonDocument.Parse(frame!.Value.GetRawText()))
            {
                var payload = doc.RootElement.GetProperty("payload");
                Assert.Equal("CHALLENGE_REJECTED", doc.RootElement.GetProperty("type").GetString());
                Assert.Equal(challengeId, payload.GetProperty("challengeId").GetString());
                Assert.Equal(rejectedByPlayerId, payload.GetProperty("rejectedByPlayerId").GetString());
                Assert.Equal(ChallengeStatus.REJECTED.ToString(), payload.GetProperty("status").GetString());
            }
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
            return JsonDocument.Parse(loginResult!.Value.GetRawText())
                .RootElement.GetProperty("payload").GetProperty("player").GetProperty("playerId").GetString()!;
        }

        private static string SeedPendingChallenge(ChallengeManager challenges, string challengerPlayerId, string targetPlayerId)
        {
            var send = challenges.SendChallenge(
                challengerPlayerId,
                targetPlayerId,
                "STANDARD_PRO",
                DateTimeOffset.UtcNow,
                TimeSpan.FromMinutes(1));
            Assert.True(send.IsSuccess, $"Seeding challenge should succeed: {send.Message}");
            return send.Challenge!.ChallengeId;
        }

        private static Task SendChallengeAsync(NetworkStream stream, string challengerPlayerId, string targetPlayerId)
        {
            return SendAsync(stream, new
            {
                protocolVersion = "1.0",
                type = "CHALLENGE_SEND",
                requestId = $"01J{challengerPlayerId}CHAL",
                sessionToken = challengerPlayerId,
                roomId = (string?)null,
                clientSequence = 3L,
                sentAtUtc = DateTimeOffset.UtcNow,
                payload = new { targetPlayerId, timeProfile = "STANDARD_PRO" }
            });
        }

        private static async Task SendRejectAsync(NetworkStream stream, string rejectingPlayerId, string challengeId)
        {
            var reject = new
            {
                protocolVersion = "1.0",
                type = "CHALLENGE_REJECT",
                requestId = $"01J{rejectingPlayerId}REJT",
                sessionToken = rejectingPlayerId,
                roomId = challengeId,
                clientSequence = 4L,
                sentAtUtc = DateTimeOffset.UtcNow,
                payload = new { challengeId }
            };
            await SendAsync(stream, reject);
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
}
