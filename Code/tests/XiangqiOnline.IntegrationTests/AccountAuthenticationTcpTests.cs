using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using XiangqiOnline.Persistence.Configuration;
using XiangqiOnline.Persistence.Services;
using XiangqiOnline.Server.Accounts;
using XiangqiOnline.Server.Lobby;
using XiangqiOnline.Server.Networking;

namespace XiangqiOnline.IntegrationTests;

public sealed class AccountAuthenticationTcpTests
{
    [Fact]
    public async Task Register_Reset_AndLogin_WorkAcrossRealTcp()
    {
        var path = Path.Combine(Path.GetTempPath(), "xiangqi-auth-tcp-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var options = new DatabaseOptions { DatabasePath = path };
            new GamePersistenceService(options, NullLoggerFactory.Instance).InitializeDatabase();
            var directory = new PlayerSessionDirectory();
            var router = new MessageRouter();
            router.Register("HELLO", HelloMessageHandler.HandleAsync);
            var challenges = new ChallengeManager(directory);
            await using var server = new GameServerHost("127.0.0.1", 0, router, directory);
            LobbyMessageRoutes.Register(router, directory, challenges, server);
            var mail = new FakeEmail();
            var accounts = new AccountService(options, "integration-reset-pepper-32-chars");
            LobbyMessageRoutes.RegisterAccounts(router, new AccountMessageHandler(accounts, mail, directory));
            await server.StartAsync();

            using (var registration = await ConnectAsync(server.BoundPort!.Value))
            {
                await SendAsync(registration.GetStream(), "ACCOUNT_REGISTER_REQUEST", 2, new { email = "tcp@example.com", displayName = "TcpUser", password = "Abcd1234" });
                Assert.Equal("ACCOUNT_REGISTER_RESULT", (await ReadTypeAsync(registration.GetStream(), "ACCOUNT_REGISTER_RESULT")).GetProperty("type").GetString());
                Assert.Equal("LOGIN_RESULT", (await ReadTypeAsync(registration.GetStream(), "LOGIN_RESULT")).GetProperty("type").GetString());
            }

            using (var reset = await ConnectAsync(server.BoundPort.Value))
            {
                await SendAsync(reset.GetStream(), "PASSWORD_RESET_REQUEST", 2, new { email = "tcp@example.com" });
                Assert.Equal("PASSWORD_RESET_SENT", (await ReadTypeAsync(reset.GetStream(), "PASSWORD_RESET_SENT")).GetProperty("type").GetString());
                Assert.Matches("^[0-9]{6}$", mail.Code!);
                await SendAsync(reset.GetStream(), "PASSWORD_RESET_CONFIRM", 3, new { email = "tcp@example.com", code = mail.Code, newPassword = "Newpass9" });
                var result = await ReadTypeAsync(reset.GetStream(), "PASSWORD_RESET_RESULT");
                Assert.Equal("ACCEPTED", result.GetProperty("payload").GetProperty("status").GetString());
            }

            Assert.True(accounts.Authenticate("tcp@example.com", "Newpass9").Success);
            Assert.False(accounts.Authenticate("tcp@example.com", "Abcd1234").Success);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static async Task<TcpClient> ConnectAsync(int port)
    {
        var client = new TcpClient(); await client.ConnectAsync("127.0.0.1", port);
        await SendAsync(client.GetStream(), "HELLO", 1, new { protocolVersion = "1.0", clientName = "TEST" });
        Assert.Equal("HELLO_ACK", (await ReadTypeAsync(client.GetStream(), "HELLO_ACK")).GetProperty("type").GetString()); return client;
    }
    private static async Task SendAsync(NetworkStream stream, string type, long sequence, object payload)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { protocolVersion = "1.0", type, requestId = Guid.NewGuid().ToString("N"), sessionToken = (string?)null, roomId = (string?)null, clientSequence = sequence, sentAtUtc = DateTimeOffset.UtcNow, payload }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var header = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(header, bytes.Length); await stream.WriteAsync(header); await stream.WriteAsync(bytes); await stream.FlushAsync();
    }
    private static async Task<JsonElement> ReadAsync(NetworkStream stream)
    {
        var header = new byte[4]; await stream.ReadExactlyAsync(header); var bytes = new byte[BinaryPrimitives.ReadInt32BigEndian(header)]; await stream.ReadExactlyAsync(bytes); using var doc = JsonDocument.Parse(bytes); return doc.RootElement.Clone();
    }
    private static async Task<JsonElement> ReadTypeAsync(NetworkStream stream, string type)
    {
        for (var i = 0; i < 6; i++)
        {
            var message = await ReadAsync(stream);
            if (message.GetProperty("type").GetString() == type) return message;
        }
        throw new InvalidDataException($"Không nhận được event {type}.");
    }
    private sealed class FakeEmail : IPasswordResetEmailSender
    {
        public string? Code { get; private set; }
        public Task<bool> SendAsync(string recipient, string displayName, string code, CancellationToken cancellationToken) { Code = code; return Task.FromResult(true); }
    }
}
