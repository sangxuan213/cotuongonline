using System.Threading;
using System.Threading.Tasks;
using XiangqiOnline.Shared.Transport;
using Xunit;

namespace XiangqiOnline.Shared.Tests.Transport;

public class HeartbeatMonitorTests
{
    // Settings được thu nhỏ hàng chục lần so với production để test chạy nhanh
    // (poll 10ms thay vì 1000ms) — tỉ lệ interval/timeout giữ nguyên như thật.
    private static HeartbeatSettings FastSettings(int intervalMs = 50, int timeoutMs = 150, int pollMs = 10) =>
        new()
        {
            HeartbeatIntervalMs = intervalMs,
            HeartbeatTimeoutMs = timeoutMs,
            TransportReadTimeoutMs = timeoutMs + 1000, // chỉ cần > timeout để Validate() không ném
            PollIntervalMs = pollMs
        };

    [Fact]
    public async Task Start_WhenIdleBeyondInterval_SendsPing()
    {
        int pingCount = 0;
        var monitor = new HeartbeatMonitor(FastSettings(), _ => { Interlocked.Increment(ref pingCount); return Task.CompletedTask; });
        monitor.Start();

        await Task.Delay(120); // > interval (50ms), < timeout (150ms)

        Assert.True(Interlocked.CompareExchange(ref pingCount, 0, 0) >= 1);
        await monitor.DisposeAsync();
    }

    [Fact]
    public async Task Start_NoActivityBeyondTimeout_RaisesTimedOutExactlyOnce()
    {
        var monitor = new HeartbeatMonitor(FastSettings(), _ => Task.CompletedTask);
        int timedOutCount = 0;
        monitor.TimedOut += () => Interlocked.Increment(ref timedOutCount);
        monitor.Start();

        await Task.Delay(300); // > timeout (150ms), đủ lâu để chắc chắn đã bắn timeout

        Assert.Equal(1, Interlocked.CompareExchange(ref timedOutCount, 0, 0));
        await monitor.DisposeAsync();
    }

    [Fact]
    public async Task NotifyActivity_CalledRegularly_NeverRaisesTimedOut_NoFalsePositive()
    {
        // Đúng tiêu chí nghiệm thu: "không false-positive khi traffic hợp lệ" —
        // giả lập traffic thật tới đều đặn nhanh hơn cả HeartbeatIntervalMs.
        var monitor = new HeartbeatMonitor(FastSettings(), _ => Task.CompletedTask);
        bool timedOut = false;
        monitor.TimedOut += () => timedOut = true;
        monitor.Start();

        for (int i = 0; i < 10; i++)
        {
            await Task.Delay(20);
            monitor.NotifyActivity(); // traffic nghiệp vụ thật liên tục tới
        }

        Assert.False(timedOut);
        await monitor.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_CalledTwice_DoesNotThrow()
    {
        // Bug tái hiện thực tế: 'await using' tự dispose cuối scope CỘNG với 1 lần
        // gọi DisposeAsync() tay trước đó (đo thời gian dispose) — phải an toàn.
        var monitor = new HeartbeatMonitor(FastSettings(), _ => Task.CompletedTask);
        monitor.Start();

        await monitor.DisposeAsync();
        var exception = await Record.ExceptionAsync(() => monitor.DisposeAsync().AsTask());

        Assert.Null(exception);
    }

    [Fact]
    public async Task DisposeAsync_BeforeTimeout_DoesNotRaiseTimedOut()
    {
        var monitor = new HeartbeatMonitor(FastSettings(), _ => Task.CompletedTask);
        bool timedOut = false;
        monitor.TimedOut += () => timedOut = true;
        monitor.Start();

        await Task.Delay(30); // còn trong khoảng interval, chưa timeout
        await monitor.DisposeAsync();

        await Task.Delay(200); // đợi thêm — nếu loop không dừng hẳn sẽ vẫn bắn timeout ở đây
        Assert.False(timedOut);
    }

    [Fact]
    public void Constructor_TimeoutNotGreaterThanInterval_ThrowsArgumentOutOfRangeException()
    {
        var badSettings = new HeartbeatSettings
        {
            HeartbeatIntervalMs = 100,
            HeartbeatTimeoutMs = 100, // phải > interval, không được bằng
            TransportReadTimeoutMs = 5000
        };

        Assert.Throws<System.ArgumentOutOfRangeException>(() =>
            new HeartbeatMonitor(badSettings, _ => Task.CompletedTask));
    }
}
