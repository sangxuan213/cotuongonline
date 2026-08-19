using System;
using System.Net.Sockets;
using System.Text;
using XiangqiOnline.Shared.Transport;
using System.Threading.Tasks;
using XiangqiOnline.Shared.Middleware;
using XiangqiOnline.Shared.Protocol;
using Xunit;

namespace XiangqiOnline.Shared.Tests.Transport
{
    /// <summary>
    /// P2-TV1-D3 evidence: "accept loop tiếp tục chạy" — spam 1 Client tới mức bị đóng
    /// kết nối không được làm treo hay dừng vòng lặp accept của TcpServerHost; Client
    /// mới vẫn kết nối được bình thường ngay sau đó.
    /// </summary>
    public class AcceptLoopSurvivesSpamTests
    {
        [Fact]
        public async Task ServerAcceptLoop_StillAcceptsNewClient_WhileAnotherWasSpammedAndClosed()
        {
            await using var server = new TcpServerHost("127.0.0.1", 0);
            await server.StartAsync();

            var strictLimits = new RateLimiterSettings { MaxMessagesPerWindow = 2, WindowMs = 60_000, MaxViolationsBeforeClose = 3 };
            var heartbeatSettings = new HeartbeatSettings
            {
                HeartbeatIntervalMs = 10_000,
                HeartbeatTimeoutMs = 30_000,
                TransportReadTimeoutMs = 60_000
            };

            server.ClientAccepted += tcpClient =>
            {
                var connection = new HeartbeatedConnection(tcpClient.GetStream(), heartbeatSettings, strictLimits);
                connection.Start();
                // Không cần dispose thủ công trong test này — connection tự đóng khi bị spam,
                // đúng kịch bản thật (server không giữ tay lái, mỗi connection tự quản vòng đời).
            };

            // Client 1: spam liên tục cho tới khi bị server đóng kết nối vì vượt rate limit.
            using (var spamClient = new TcpClient())
            {
                await spamClient.ConnectAsync(System.Net.IPAddress.Loopback, server.BoundPort!.Value);
                var stream = spamClient.GetStream();
                for (int i = 0; i < 10; i++)
                {
                    var payload = Encoding.UTF8.GetBytes("{\"type\":\"SPAM\"}");
                    await TcpFrameCodec.WriteFrameAsync(stream, payload);
                }
                await Task.Delay(300); // đợi server xử lý + đóng kết nối do vi phạm liên tục
            }

            // Client 2: kết nối MỚI ngay sau đó — accept loop phải vẫn sống, không bị kẹt vì Client 1.
            using var newClient = new TcpClient();
            var connectTask = newClient.ConnectAsync(System.Net.IPAddress.Loopback, server.BoundPort!.Value);
            var completed = await Task.WhenAny(connectTask, Task.Delay(TimeSpan.FromSeconds(3)));

            Assert.Same(connectTask, completed);
            Assert.True(newClient.Connected);
        }
    }
}
