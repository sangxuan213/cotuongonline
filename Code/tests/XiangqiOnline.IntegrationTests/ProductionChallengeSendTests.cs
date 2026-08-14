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
    /// REAL CHALLENGE_SEND end-to-end over the shipped production path:
    /// GameServerHost -> ClientConnectionHandler -> MessageRouter ->
    /// ChallengeMessageHandler -> ChallengeManager -> CHALLENGE_RECEIVED forwarded
    /// to the target connection only. Wire format identical to TV5's GameClient.
    /// </summary>
    public class ProductionChallengeSendTests
    {
        [Fact]
        public async Task ChallengeSend_ReachesTargetConnection_WithInvitationPayload()
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

            var invitationJson = await ReadOneFrameAsync(streamB);
            Assert.NotNull(invitationJson);
            using (var doc = JsonDocument.Parse(invitationJson!.Value.GetRawText()))
            {
                var root = doc.RootElement;
                Assert.Equal("CHALLENGE_RECEIVED", root.GetProperty("type").GetString());
                var challenge = root.TryGetProperty("payload", out var p)
                    ? (p.TryGetProperty("challenge", out var c) ? c : p)
                    : root;
                Assert.Equal(alice.PlayerId, challenge.GetProperty("fromPlayerId").GetString());
                Assert.Equal("Alice", challenge.GetProperty("fromDisplayName").GetString());
                var challengeId = challenge.GetProperty("challengeId").GetString();
                Assert.False(string.IsNullOrWhiteSpace(challengeId));
                Assert.True(challenges.TryGetChallenge(challengeId!, out _));
            }

            Assert.Equal(PlayerStatus.INVITING, alice.Status);
            Assert.Equal(PlayerStatus.INVITED, bob.Status);
        }

        [Fact]
        public async Task ChallengeSend_SelfChallenge_ReturnsErrorResponse()
        {
            var (server, players, _) = CreateServer();
            await using var serverGuard = server;
            await server.StartAsync();

            using var clientA = new TcpClient();
            await clientA.ConnectAsync("127.0.0.1", server.BoundPort!.Value);
            var streamA = clientA.GetStream();

            var aliceId = await LoginAsync(streamA, "Alice");
            await SendChallengeAsync(streamA, aliceId, aliceId);

            var errorJson = await ReadOneFrameAsync(streamA);
            Assert.NotNull(errorJson);
            using (var doc = JsonDocument.Parse(errorJson!.Value.GetRawText()))
            {
                var root = doc.RootElement;
                Assert.Equal("ERROR_RESPONSE", root.GetProperty("type").GetString());
                Assert.Equal(ErrorCodes.PLAYER_NOT_AVAILABLE, root.GetProperty("payload").GetProperty("errorCode").GetString());
            }
        }

        [Fact]
        public async Task ChallengeSend_UnknownTarget_ReturnsErrorResponse()
        {
            var (server, players, _) = CreateServer();
            await using var serverGuard = server;
            await server.StartAsync();

            using var clientA = new TcpClient();
            await clientA.ConnectAsync("127.0.0.1", server.BoundPort!.Value);
            var streamA = clientA.GetStream();

            var aliceId = await LoginAsync(streamA, "Alice");
            await SendChallengeAsync(streamA, aliceId, "nobody-here");

            var errorJson = await ReadOneFrameAsync(streamA);
            Assert.NotNull(errorJson);
            using (var doc = JsonDocument.Parse(errorJson!.Value.GetRawText()))
            {
                var root = doc.RootElement;
                Assert.Equal("ERROR_RESPONSE", root.GetProperty("type").GetString());
                Assert.Equal(ErrorCodes.PLAYER_NOT_AVAILABLE, root.GetProperty("payload").GetProperty("errorCode").GetString());
            }
        }

        [Fact]
        public async Task ChallengeSend_BusyTarget_ReturnsErrorResponse()
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

            await SendChallengeAsync(streamA, alice.PlayerId, bob.PlayerId);
            var inviteForBob = await ReadOneFrameAsync(streamB);
            Assert.NotNull(inviteForBob);
            challenges.TryGetChallenge(
                JsonDocument.Parse(inviteForBob!.Value.GetRawText()).RootElement
                    .GetProperty("payload").GetProperty("challenge").GetProperty("challengeId").GetString()!,
                out _);

            await SendChallengeAsync(streamC, charlieId, bob.PlayerId);

            var errorJson = await ReadOneFrameAsync(streamC);
            Assert.NotNull(errorJson);
            using (var doc = JsonDocument.Parse(errorJson!.Value.GetRawText()))
            {
                var root = doc.RootElement;
                Assert.Equal("ERROR_RESPONSE", root.GetProperty("type").GetString());
                Assert.Equal(ErrorCodes.PLAYER_NOT_AVAILABLE, root.GetProperty("payload").GetProperty("errorCode").GetString());
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

        private static async Task SendChallengeAsync(NetworkStream stream, string challengerPlayerId, string targetPlayerId)
        {
            var challenge = new
            {
                protocolVersion = "1.0",
                type = "CHALLENGE_SEND",
                requestId = $"01J{challengerPlayerId}CHAL",
                sessionToken = challengerPlayerId,
                roomId = (string?)null,
                clientSequence = 3L,
                sentAtUtc = DateTimeOffset.UtcNow,
                payload = new { targetPlayerId, timeProfile = "STANDARD_PRO" }
            };
            await SendAsync(stream, challenge);
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
