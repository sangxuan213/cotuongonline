using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using XiangqiOnline.Shared.Protocol;
using XiangqiOnline.Shared.Transport;
using Xunit;

namespace XiangqiOnline.Shared.Tests.Transport;

/// <summary>
/// P2-TV1-D5 evidence: 2 kịch bản còn thiếu trong danh sách yêu cầu — "Server
/// shutdown" (khác với 1 client bị rớt — đây là cả server chủ động dừng) và "hai
/// command gần đồng thời" (2 lệnh gửi cùng lúc trên 1 kết nối không được lẫn byte).
/// </summary>
public class ServerShutdownAndConcurrentSendTests
{
    private static HeartbeatSettings LenientHeartbeat() => new()
    {
        HeartbeatIntervalMs = 10_000,
        HeartbeatTimeoutMs = 30_000,
        TransportReadTimeoutMs = 60_000
    };

    [Fact]
    public async Task ServerStopAsync_WhileClientConnected_StopsCleanly_DoesNotHang()
    {
        await using var server = new TcpServerHost("127.0.0.1", 0);
        await server.StartAsync();
        var port = server.BoundPort!.Value;

        using var client = new TcpClient();
        await client.ConnectAsync(System.Net.IPAddress.Loopback, port);

        // "Server shutdown" — dừng server trong khi vẫn còn client đang kết nối.
        var stopTask = server.StopAsync();
        var completed = await Task.WhenAny(stopTask, Task.Delay(TimeSpan.FromSeconds(3)));

        Assert.Same(stopTask, completed); // StopAsync phải hoàn tất, không treo vì còn client

        // Sau khi server dừng, kết nối mới phải bị từ chối (không còn ai lắng nghe ở port cũ).
        using var lateClient = new TcpClient();
        await Assert.ThrowsAnyAsync<SocketException>(() =>
            lateClient.ConnectAsync(System.Net.IPAddress.Loopback, port));
    }

    [Fact]
    public async Task TwoConcurrentSendFrameAsyncCalls_BothFramesArriveIntact_NoInterleaving()
    {
        // "Hai command gần đồng thời" — 2 nước đi/lệnh nghiệp vụ được gửi gần như
        // cùng lúc trên CÙNG 1 kết nối. Nếu write-lock sai, byte của 2 frame có thể
        // lẫn vào nhau trên wire -> phía nhận sẽ parse ra rác hoặc lỗi framing.
        var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var acceptTask = listener.AcceptTcpClientAsync();

        using var client = new TcpClient();
        await client.ConnectAsync(System.Net.IPAddress.Loopback, ((System.Net.IPEndPoint)listener.LocalEndpoint).Port);
        using var serverSide = await acceptTask;

        await using var senderConnection = new HeartbeatedConnection(client.GetStream(), LenientHeartbeat());
        senderConnection.Start();

        var payloadA = Encoding.UTF8.GetBytes("{\"type\":\"MOVE\",\"from\":\"A\"}");
        var payloadB = Encoding.UTF8.GetBytes("{\"type\":\"MOVE\",\"from\":\"B\"}");

        // Gửi đồng thời — cố tình KHÔNG await tuần tự để tối đa hoá khả năng race thật.
        var sendA = senderConnection.SendFrameAsync(payloadA);
        var sendB = senderConnection.SendFrameAsync(payloadB);
        await Task.WhenAll(sendA, sendB);

        var serverStream = serverSide.GetStream();
        var received1 = await TcpFrameCodec.ReadFrameAsync(serverStream);
        var received2 = await TcpFrameCodec.ReadFrameAsync(serverStream);

        Assert.NotNull(received1);
        Assert.NotNull(received2);

        var receivedTexts = new[] { Encoding.UTF8.GetString(received1!), Encoding.UTF8.GetString(received2!) };
        // Cả 2 payload gốc phải xuất hiện NGUYÊN VẸN (thứ tự không quan trọng, miễn không lẫn byte).
        Assert.Contains(Encoding.UTF8.GetString(payloadA), receivedTexts);
        Assert.Contains(Encoding.UTF8.GetString(payloadB), receivedTexts);
    }
}
