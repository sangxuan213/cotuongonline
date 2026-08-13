using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;
using XiangqiOnline.Server.Lobby;
using XiangqiOnline.Server.Networking;
using XiangqiOnline.Shared.Protocol;
using XiangqiOnline.Shared.Transport;
using Xunit;

namespace XiangqiOnline.IntegrationTests
{
    /// <summary>
    /// REAL production HELLO/HELLO_ACK handshake through the exact path Program.cs ships:
    /// GameServerHost -> TcpServerHost accept -> ClientConnectionHandler ->
    /// ConnectionReceiveLoop -> framing -> MessageRouter -> HELLO route -> HELLO_ACK.
    /// No mocked stream, no fake dispatcher, no test-only shortcut.
    /// </summary>
    public class ProductionHelloHandshakeTests
    {
        [Fact]
        public async Task RealServer_RespondsWithHelloAck_ToRealHelloFrame()
        {
            await using var server = CreateServer();
            await server.StartAsync();

            using var client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", server.BoundPort!.Value);

            var stream = client.GetStream();

            // Wire format identical to TV5 GameClient.SendAsync("HELLO", ...).
            var hello = new
            {
                protocolVersion = "1.0",
                type = "HELLO",
                requestId = "01J000000000000000000HELLO",
                sessionToken = (string?)null,
                roomId = (string?)null,
                clientSequence = 1L,
                sentAtUtc = DateTimeOffset.UtcNow,
                payload = new { protocolVersion = "1.0", clientName = "UDM18.WPF" }
            };

            await FakeTv5Client.SendAsync(stream, hello);

            var ackJson = await FakeTv5Client.ReadOneFrameAsync(stream);
            Assert.NotNull(ackJson);

            using var doc = JsonDocument.Parse(ackJson!.Value.GetRawText());
            var root = doc.RootElement;

            Assert.Equal("HELLO_ACK", root.GetProperty("type").GetString());
            Assert.Equal("1.0", root.GetProperty("protocolVersion").GetString());
            Assert.Equal("01J000000000000000000HELLO",
                root.GetProperty("causationRequestId").GetString());
            Assert.Equal("1.0", root.GetProperty("payload").GetProperty("supportedVersion").GetString());
        }

        [Fact]
        public async Task RealServer_IdempotentHelloAck_RepliesToEachHello()
        {
            await using var server = CreateServer();
            await server.StartAsync();

            using var client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", server.BoundPort!.Value);
            var stream = client.GetStream();

            for (int i = 0; i < 2; i++)
            {
                var hello = new
                {
                    protocolVersion = "1.0",
                    type = "HELLO",
                    requestId = $"01J000000000000000000{(i + 1):D5}",
                    sessionToken = (string?)null,
                    roomId = (string?)null,
                    clientSequence = i + 1L,
                    sentAtUtc = DateTimeOffset.UtcNow,
                    payload = new { protocolVersion = "1.0", clientName = "UDM18.WPF" }
                };

                await FakeTv5Client.SendAsync(stream, hello);
                var ack = await FakeTv5Client.ReadOneFrameAsync(stream);
                Assert.NotNull(ack);
            }
        }

        [Fact]
        public async Task RealServer_Login_RegistersPlayerAndAppearsInPlayerList()
        {
            await using var server = CreateServer();
            await server.StartAsync();

            using var client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", server.BoundPort!.Value);
            var stream = client.GetStream();

            var hello = new
            {
                protocolVersion = "1.0",
                type = "HELLO",
                requestId = "01J000000000000000000HELLO",
                sessionToken = (string?)null,
                roomId = (string?)null,
                clientSequence = 1L,
                sentAtUtc = DateTimeOffset.UtcNow,
                payload = new { protocolVersion = "1.0", clientName = "UDM18.WPF" }
            };
            await FakeTv5Client.SendAsync(stream, hello);
            var ack = await FakeTv5Client.ReadOneFrameAsync(stream);
            Assert.NotNull(ack);

            var login = new
            {
                protocolVersion = "1.0",
                type = "LOGIN_REQUEST",
                requestId = "01J000000000000000000LOGIN",
                sessionToken = (string?)null,
                roomId = (string?)null,
                clientSequence = 2L,
                sentAtUtc = DateTimeOffset.UtcNow,
                payload = new { displayName = "Tester", resumeToken = (string?)null }
            };
            await FakeTv5Client.SendAsync(stream, login);

            var loginResultJson = await FakeTv5Client.ReadOneFrameAsync(stream);
            Assert.NotNull(loginResultJson);
            using (var doc = JsonDocument.Parse(loginResultJson!.Value.GetRawText()))
            {
                var root = doc.RootElement;
                Assert.Equal("LOGIN_RESULT", root.GetProperty("type").GetString());
                Assert.Equal("01J000000000000000000LOGIN", root.GetProperty("causationRequestId").GetString());
                Assert.Equal("ACCEPTED", root.GetProperty("payload").GetProperty("status").GetString());
                Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("payload").GetProperty("token").GetString()));
                Assert.Equal("Tester", root.GetProperty("payload").GetProperty("player").GetProperty("displayName").GetString());
                Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("payload").GetProperty("player").GetProperty("playerId").GetString()));
            }

            var token = JsonDocument.Parse(loginResultJson!.Value.GetRawText())
                .RootElement.GetProperty("payload").GetProperty("token").GetString();

            var playerListRequest = new
            {
                protocolVersion = "1.0",
                type = "PLAYER_LIST_REQUEST",
                requestId = "01J000000000000000000PLIST",
                sessionToken = token,
                roomId = (string?)null,
                clientSequence = 3L,
                sentAtUtc = DateTimeOffset.UtcNow,
                payload = new { }
            };
            await FakeTv5Client.SendAsync(stream, playerListRequest);

            var playerListJson = await FakeTv5Client.ReadOneFrameAsync(stream);
            Assert.NotNull(playerListJson);
            using (var doc = JsonDocument.Parse(playerListJson!.Value.GetRawText()))
            {
                var root = doc.RootElement;
                Assert.Equal("PLAYER_LIST_UPDATED", root.GetProperty("type").GetString());
                var players = root.GetProperty("payload").GetProperty("players").EnumerateArray().ToArray();
                var tester = players.SingleOrDefault(p => p.GetProperty("displayName").GetString() == "Tester");
                Assert.NotNull(tester);
                Assert.Equal("AVAILABLE", tester.GetProperty("status").GetString());
            }
        }

        private static GameServerHost CreateServer()
        {
            var router = new MessageRouter();
            router.Register("HELLO", HelloMessageHandler.HandleAsync);
            LobbyMessageRoutes.Register(router, new PlayerSessionDirectory());
            return new GameServerHost("127.0.0.1", 0, router);
        }

        /// <summary>
        /// Read/write bytes exactly like TV5 TcpProtocolTransport (SendAsync /
        /// ReceiveLoopAsync) — copied here because the WPF Client project is not
        /// referenced by this test assembly.
        /// </summary>
        private static class FakeTv5Client
        {
            private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

            public static async Task SendAsync(NetworkStream stream, object envelope)
            {
                var payload = JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions);
                var header = new byte[4];
                BinaryPrimitives.WriteInt32BigEndian(header, payload.Length);
                await stream.WriteAsync(header);
                await stream.WriteAsync(payload);
                await stream.FlushAsync();
            }

            public static async Task<JsonElement?> ReadOneFrameAsync(NetworkStream stream)
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
        }
    }
}