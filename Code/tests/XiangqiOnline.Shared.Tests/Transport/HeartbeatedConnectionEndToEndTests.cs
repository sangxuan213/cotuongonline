using System;
using System.Net.Sockets;
using System.Threading.Tasks;
using XiangqiOnline.Shared.Transport;
using Xunit;

namespace XiangqiOnline.Shared.Tests.Transport;

/// <summary>
/// P2-TV1-D1 evidence: test trên socket TCP loopback THẬT (không phải MemoryStream),
/// mô phỏng đúng kịch bản nghiệm thu "mất heartbeat chuyển đúng trạng thái" +
/// "task cleanup khi disconnect".
/// </summary>
public class HeartbeatedConnectionEndToEndTests
{
    private static HeartbeatSettings FastSettings() => new()
    {
        HeartbeatIntervalMs = 50,
        HeartbeatTimeoutMs = 150,
        TransportReadTimeoutMs = 5000, // rộng — bài test này nhắm heartbeat timeout, không phải transport timeout
        PollIntervalMs = 10
    };

    [Fact]
    public async Task PeerGoesSilent_ServerSideDetectsHeartbeatTimeout_AndDisposeCompletesPromptly()
    {
        using var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var acceptTask = listener.AcceptTcpClientAsync();

        using var client = new TcpClient();
        await client.ConnectAsync(System.Net.IPAddress.Loopback, ((System.Net.IPEndPoint)listener.LocalEndpoint).Port);
        using var serverSideClient = await acceptTask;

        // Client kết nối xong rồi im lặng hoàn toàn — không gửi gì, không đọc gì —
        // đúng kịch bản "mất heartbeat" (peer treo/crash nhưng socket TCP chưa kịp báo FIN/RST).
        await using var connection = new HeartbeatedConnection(serverSideClient.GetStream(), FastSettings());

        string? closeReason = null;
        var closedTcs = new TaskCompletionSource();
        connection.Closed += reason => { closeReason = reason; closedTcs.TrySetResult(); };

        connection.Start();

        await closedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("HEARTBEAT_TIMEOUT", closeReason);

        // "Task cleanup khi disconnect": DisposeAsync phải hoàn tất nhanh, không treo —
        // nếu receive-loop task hoặc heartbeat loop bị rò, DisposeAsync sẽ hang ở đây.
        var disposeTask = connection.DisposeAsync().AsTask();
        var completed = await Task.WhenAny(disposeTask, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.Same(disposeTask, completed);
    }

    [Fact]
    public async Task BothSidesExchangeHeartbeat_ConnectionStaysAlive_NoTimeout()
    {
        using var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var acceptTask = listener.AcceptTcpClientAsync();

        using var client = new TcpClient();
        await client.ConnectAsync(System.Net.IPAddress.Loopback, ((System.Net.IPEndPoint)listener.LocalEndpoint).Port);
        using var serverSideClient = await acceptTask;

        await using var serverConnection = new HeartbeatedConnection(serverSideClient.GetStream(), FastSettings());
        await using var clientConnection = new HeartbeatedConnection(client.GetStream(), FastSettings());

        bool anyClosed = false;
        serverConnection.Closed += _ => anyClosed = true;
        clientConnection.Closed += _ => anyClosed = true;

        serverConnection.Start();
        clientConnection.Start();

        // "Heartbeat hai chiều": cả 2 bên chỉ tự gửi PING theo đồng hồ idle của mình,
        // không bên nào chủ động gửi traffic nghiệp vụ — vẫn phải sống sót qua nhiều
        // chu kỳ interval nhờ nhận PING từ phía kia.
        await Task.Delay(400); // ~2-3 lần HeartbeatIntervalMs (50ms)

        Assert.False(anyClosed);
    }
}
