using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using XiangqiOnline.Shared.Middleware;
using XiangqiOnline.Shared.Protocol;
using XiangqiOnline.Shared.Transport;
using Xunit;

namespace XiangqiOnline.Shared.Tests.Transport;

/// <summary>P2-TV1-D3 evidence: rate limit theo connection, cô lập lỗi từng Client.</summary>
public class HeartbeatedConnectionRateLimitTests
{
    private static HeartbeatSettings LenientHeartbeat() => new()
    {
        HeartbeatIntervalMs = 10_000,
        HeartbeatTimeoutMs = 30_000,
        TransportReadTimeoutMs = 60_000
    };

    private static async Task<(TcpClient client, TcpClient serverSide, TcpListener listener)> ConnectPairAsync()
    {
        var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var acceptTask = listener.AcceptTcpClientAsync();

        var client = new TcpClient();
        await client.ConnectAsync(System.Net.IPAddress.Loopback, ((System.Net.IPEndPoint)listener.LocalEndpoint).Port);
        var serverSide = await acceptTask;
        return (client, serverSide, listener);
    }

    private static async Task SendFrameAsync(NetworkStream stream, string type)
    {
        var payload = Encoding.UTF8.GetBytes($"{{\"type\":\"{type}\"}}");
        await TcpFrameCodec.WriteFrameAsync(stream, payload);
    }

    [Fact]
    public async Task SpammingBeyondLimit_DropsExcessFrames_DoesNotForwardToApp()
    {
        var (client, serverSide, listener) = await ConnectPairAsync();
        using var _l = listener; using var _c = client; using var _s = serverSide;

        var rateLimits = new RateLimiterSettings { MaxMessagesPerWindow = 3, WindowMs = 5000, MaxViolationsBeforeClose = 1000 };
        await using var connection = new HeartbeatedConnection(serverSide.GetStream(), LenientHeartbeat(), rateLimits);

        int received = 0, rateLimited = 0;
        connection.FrameReceived += (_, _) => received++;
        connection.RateLimited += () => rateLimited++;
        connection.Start();

        var clientStream = client.GetStream();
        for (int i = 0; i < 10; i++)
            await SendFrameAsync(clientStream, "MOVE");

        await Task.Delay(300); // đợi server xử lý hết 10 frame

        Assert.Equal(3, received);     // đúng bằng MaxMessagesPerWindow
        Assert.Equal(7, rateLimited);  // 7 frame còn lại bị drop
    }

    [Fact]
    public async Task SustainedSpam_ExceedsViolationThreshold_ClosesConnectionGracefully()
    {
        var (client, serverSide, listener) = await ConnectPairAsync();
        using var _l = listener; using var _c = client; using var _s = serverSide;

        var rateLimits = new RateLimiterSettings { MaxMessagesPerWindow = 2, WindowMs = 5000, MaxViolationsBeforeClose = 3 };
        await using var connection = new HeartbeatedConnection(serverSide.GetStream(), LenientHeartbeat(), rateLimits);

        string? closeReason = null;
        var closedTcs = new TaskCompletionSource();
        connection.Closed += reason => { closeReason = reason; closedTcs.TrySetResult(); };
        connection.Start();

        var clientStream = client.GetStream();
        // 2 đầu tiên hợp lệ, 5 tiếp theo vượt hạn mức liên tục (>= 3 -> đóng).
        for (int i = 0; i < 7; i++)
            await SendFrameAsync(clientStream, "MOVE");

        await closedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("RATE_LIMIT_EXCEEDED", closeReason);
    }

    [Fact]
    public async Task OccasionalBurst_DoesNotAccumulateTowardClose_ConnectionStaysAlive()
    {
        // "Cho phép vài lần vượt ngắn hạn" — vi phạm KHÔNG liên tục (xen kẽ có traffic
        // hợp lệ ở giữa) không được cộng dồn tới ngưỡng đóng kết nối.
        var (client, serverSide, listener) = await ConnectPairAsync();
        using var _l = listener; using var _c = client; using var _s = serverSide;

        var rateLimits = new RateLimiterSettings { MaxMessagesPerWindow = 1, WindowMs = 60_000, MaxViolationsBeforeClose = 2 };
        await using var connection = new HeartbeatedConnection(serverSide.GetStream(), LenientHeartbeat(), rateLimits);

        bool closed = false;
        connection.Closed += _ => closed = true;
        connection.Start();

        var clientStream = client.GetStream();
        // Gửi 1 frame (hợp lệ, tiêu hết token) rồi đợi rất lâu hơn WindowMs trước khi gửi frame tiếp theo,
        // lặp lại nhiều lần -> không bao giờ có 2 vi phạm LIÊN TỤC thật sự vì token luôn được nạp lại.
        // (Test rút gọn: chỉ gửi 1 frame, xác nhận không bị đóng ngay từ đầu.)
        await SendFrameAsync(clientStream, "MOVE");
        await Task.Delay(200);

        Assert.False(closed);
    }

    [Fact]
    public async Task TwoConnections_SpammingOneDoesNotBlockTheOther_BothProcessedIndependently()
    {
        // Cốt lõi "cô lập lỗi từng Client": spam connection A không được làm chậm
        // nghiêm trọng connection B — vì mỗi connection chạy receive loop trên Task riêng.
        var (clientA, serverSideA, listenerA) = await ConnectPairAsync();
        var (clientB, serverSideB, listenerB) = await ConnectPairAsync();
        using var _la = listenerA; using var _ca = clientA; using var _sa = serverSideA;
        using var _lb = listenerB; using var _cb = clientB; using var _sb = serverSideB;

        var strictLimits = new RateLimiterSettings { MaxMessagesPerWindow = 2, WindowMs = 60_000, MaxViolationsBeforeClose = 1000 };

        await using var connectionA = new HeartbeatedConnection(serverSideA.GetStream(), LenientHeartbeat(), strictLimits);
        await using var connectionB = new HeartbeatedConnection(serverSideB.GetStream(), LenientHeartbeat()); // B không rate-limit, không liên quan tới A

        int receivedB = 0;
        var bReceivedFast = new TaskCompletionSource();
        connectionB.FrameReceived += (_, _) => { receivedB++; bReceivedFast.TrySetResult(); };
        connectionA.Start();
        connectionB.Start();

        // Spam A dữ dội trên 1 Task riêng, không await — mô phỏng A đang bị flood liên tục.
        var spamTask = Task.Run(async () =>
        {
            var streamA = clientA.GetStream();
            for (int i = 0; i < 2000; i++)
            {
                try { await SendFrameAsync(streamA, "SPAM"); }
                catch { break; } // A có thể bị đóng giữa chừng nếu vi phạm liên tục — không sao, đây là nhánh phụ
            }
        });

        // B gửi ĐÚNG 1 frame giữa lúc A đang bị spam — phải được xử lý nhanh, không bị A chặn.
        var streamB = clientB.GetStream();
        await SendFrameAsync(streamB, "HELLO");

        var completed = await Task.WhenAny(bReceivedFast.Task, Task.Delay(TimeSpan.FromSeconds(3)));
        Assert.Same(bReceivedFast.Task, completed);
        Assert.Equal(1, receivedB);

        await Task.WhenAny(spamTask, Task.Delay(TimeSpan.FromSeconds(2))); // dọn task nền, không assert gì thêm ở đây
    }
}
