using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using XiangqiOnline.Shared.Protocol;
using XiangqiOnline.Shared.Transport;
using Xunit;

namespace XiangqiOnline.Shared.Tests.Transport;

/// <summary>P2-TV1-D4 evidence: resilience khi Server ngắt đột ngột, cancellation propagation, correlation id.</summary>
public class HeartbeatedConnectionResilienceTests
{
    private static HeartbeatSettings LenientHeartbeat() => new()
    {
        HeartbeatIntervalMs = 10_000,
        HeartbeatTimeoutMs = 30_000,
        TransportReadTimeoutMs = 60_000
    };

    [Fact]
    public async Task UnexpectedStreamException_RaisesFaulted_ThenClosedExactlyOnce_NoUnobservedException()
    {
        var stream = new ThrowingStream();
        await using var connection = new HeartbeatedConnection(stream, LenientHeartbeat());

        Exception? faultedException = null;
        var faultedCount = 0;
        string? closeReason = null;
        var closedTcs = new TaskCompletionSource();

        connection.Faulted += ex => { faultedException = ex; Interlocked.Increment(ref faultedCount); };
        connection.Closed += reason => { closeReason = reason; closedTcs.TrySetResult(); };
        connection.Start();

        await closedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, faultedCount);
        Assert.IsType<IOException>(faultedException);
        Assert.StartsWith("UNEXPECTED_ERROR:", closeReason);

        // "Không background exception chưa quan sát" — DisposeAsync phải hoàn tất êm,
        // không ném lại exception nào (đã được Faulted "quan sát" và xử lý rồi).
        var exception = await Record.ExceptionAsync(() => connection.DisposeAsync().AsTask());
        Assert.Null(exception);
    }

    [Fact]
    public async Task SendFrameAsync_AfterConnectionClosed_FailsFastWithCancellation_DoesNotHang()
    {
        using var ms = new System.IO.MemoryStream(); // EOF ngay -> Closed("DISCONNECTED") gần như tức thì
        await using var connection = new HeartbeatedConnection(ms, LenientHeartbeat());

        var closedTcs = new TaskCompletionSource();
        connection.Closed += _ => closedTcs.TrySetResult();
        connection.Start();

        await closedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var sendTask = connection.SendFrameAsync(Encoding.UTF8.GetBytes("{\"type\":\"MOVE\"}"));
        var completed = await Task.WhenAny(sendTask, Task.Delay(TimeSpan.FromSeconds(2)));

        Assert.Same(sendTask, completed); // không bị treo
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sendTask);
    }

    [Fact]
    public void ConnectionId_IsUniquePerInstance_AndStableAcrossLifetime()
    {
        using var streamA = new System.IO.MemoryStream();
        using var streamB = new System.IO.MemoryStream();
        var connectionA = new HeartbeatedConnection(streamA, LenientHeartbeat());
        var connectionB = new HeartbeatedConnection(streamB, LenientHeartbeat());

        Assert.False(string.IsNullOrEmpty(connectionA.ConnectionId));
        Assert.NotEqual(connectionA.ConnectionId, connectionB.ConnectionId);

        var idBeforeStart = connectionA.ConnectionId;
        connectionA.Start();
        Assert.Equal(idBeforeStart, connectionA.ConnectionId); // ổn định suốt vòng đời, dùng làm correlation id được
    }

    [Fact]
    public async Task ServerClosesSocketAbruptly_ClientDetectsDisconnect_ExactlyOnce_ResourcesDisposedCleanly()
    {
        // Đúng kịch bản Ngày 4: "Server ngắt khi Client đang ở lobby/game" — mô phỏng
        // bằng cách phía server đóng socket đột ngột trong khi client vẫn đang chạy.
        var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var acceptTask = listener.AcceptTcpClientAsync();

        using var client = new TcpClient();
        await client.ConnectAsync(System.Net.IPAddress.Loopback, ((System.Net.IPEndPoint)listener.LocalEndpoint).Port);
        var serverSide = await acceptTask;

        await using var clientConnection = new HeartbeatedConnection(client.GetStream(), LenientHeartbeat());

        int closedCount = 0;
        string? closeReason = null;
        var closedTcs = new TaskCompletionSource();
        clientConnection.Closed += reason =>
        {
            Interlocked.Increment(ref closedCount);
            closeReason = reason;
            closedTcs.TrySetResult();
        };
        clientConnection.Start();

        // "Server ngắt" — đóng socket phía server đột ngột, không handshake FIN đàng hoàng.
        serverSide.Close();
        listener.Stop();

        await closedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, closedCount); // đúng 1 lần — "trạng thái Client đồng nhất", UI không nhận 2 thông báo mâu thuẫn
        Assert.NotNull(closeReason); // có lý do rõ ràng (DISCONNECTED hoặc UNEXPECTED_ERROR tuỳ hệ điều hành đóng socket kiểu gì)

        // "Resource được dispose" — DisposeAsync hoàn tất nhanh, không treo.
        var disposeTask = clientConnection.DisposeAsync().AsTask();
        var completed = await Task.WhenAny(disposeTask, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.Same(disposeTask, completed);
    }
}
