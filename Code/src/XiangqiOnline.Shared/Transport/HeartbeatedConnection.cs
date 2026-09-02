using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using XiangqiOnline.Shared.Middleware;
using XiangqiOnline.Shared.Protocol;

namespace XiangqiOnline.Shared.Transport;

/// <summary>
/// P2-TV1-D1 deliverable "Heartbeat service": kết hợp <see cref="ConnectionReceiveLoop"/>
/// (đọc + transport timeout) và <see cref="HeartbeatMonitor"/> (heartbeat nghiệp vụ)
/// thành 1 vòng đời duy nhất cho MỘT kết nối, ở cả 2 phía Server và Client (class này
/// không quan tâm ai là server/client — chỉ cần 1 Stream 2 chiều).
///
/// Đảm nhiệm luôn việc "task cleanup khi disconnect" (tiêu chí nghiệm thu): dù đóng
/// vì lý do gì (protocol violation, disconnect sạch, transport timeout, hay heartbeat
/// timeout), <see cref="Closed"/> chỉ raise đúng 1 lần, receive-loop task luôn được
/// await tới khi xong, và HeartbeatMonitor luôn được Dispose — không rò task/timer.
///
/// Cũng là nơi duy nhất được ghi (write) vào Stream — cả PING của heartbeat lẫn
/// frame nghiệp vụ của tầng ứng dụng đều phải đi qua <see cref="SendFrameAsync"/>,
/// để tránh 2 luồng ghi đè byte lên nhau trên cùng 1 socket (1 lock ghi duy nhất).
///
/// P2-TV1-D3: nếu truyền <see cref="RateLimiterSettings"/>, mỗi connection tự có 1
/// token bucket RIÊNG — spam ở 1 connection chỉ ảnh hưởng chính nó (frame vượt hạn
/// mức bị drop, không forward lên tầng ứng dụng), không làm chậm hay chặn các
/// connection khác, vì receive loop của mỗi connection chạy độc lập trên Task riêng.
/// Vượt rate limit LIÊN TỤC quá ngưỡng -> đóng kết nối (graceful, qua CloseOnce).
/// </summary>
public sealed class HeartbeatedConnection : IAsyncDisposable
{
    private readonly Stream _stream;
    private readonly ConnectionReceiveLoop _receiveLoop;
    private readonly HeartbeatMonitor _heartbeat;
    private readonly TokenBucketRateLimiter? _rateLimiter;
    private readonly int _maxViolationsBeforeClose;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private Task? _receiveLoopTask;
    private int _closed;   // guard chống Closed event raise 2 lần
    private int _disposed; // guard chống Dispose logic chạy 2 lần (tách riêng _closed)
    private int _consecutiveRateViolations;

    /// <summary>
    /// P2-TV1-D4: id ổn định trong suốt vòng đời connection, dùng làm correlation id
    /// khi log lỗi network — cho phép nối các dòng log rời rạc (receive loop, heartbeat,
    /// rate limiter) của CÙNG 1 kết nối lại với nhau khi debug sự cố thật.
    /// </summary>
    public string ConnectionId { get; } = Guid.NewGuid().ToString("N");

    /// <summary>Frame nghiệp vụ hợp lệ (KHÔNG bao gồm PING/PONG — đã bị lọc ở đây).</summary>
    public event Action<byte[], string>? FrameReceived;

    /// <summary>P2-TV1-D3: raised mỗi lần 1 frame bị drop vì vượt rate limit (frame đó KHÔNG tới FrameReceived).</summary>
    public event Action? RateLimited;

    /// <summary>
    /// P2-TV1-D4: raised khi receive loop gặp lỗi KHÔNG LƯỜNG TRƯỚC (không phải
    /// protocol violation, transport timeout, hay disconnect sạch — những cái đó đã
    /// có nhánh xử lý riêng). Luôn raised TRƯỚC Closed("UNEXPECTED_ERROR:...") đúng
    /// 1 lần, để tầng gọi log lỗi kèm <see cref="ConnectionId"/> làm correlation id.
    /// </summary>
    public event Action<Exception>? Faulted;

    /// <summary>
    /// Raised đúng 1 lần khi kết nối đóng, dù vì lý do gì:
    /// "PROTOCOL_VIOLATION:&lt;code&gt;" | "DISCONNECTED" | "TRANSPORT_TIMEOUT" | "HEARTBEAT_TIMEOUT" | "RATE_LIMIT_EXCEEDED" | "UNEXPECTED_ERROR:&lt;type&gt;" | "DISPOSED".
    /// </summary>
    public event Action<string>? Closed;

    public HeartbeatedConnection(Stream stream, HeartbeatSettings settings, RateLimiterSettings? rateLimiterSettings = null)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();

        if (rateLimiterSettings is not null)
        {
            rateLimiterSettings.Validate();
            _rateLimiter = new TokenBucketRateLimiter(rateLimiterSettings);
            _maxViolationsBeforeClose = rateLimiterSettings.MaxViolationsBeforeClose;
        }

        _receiveLoop = new ConnectionReceiveLoop(stream, settings.TransportReadTimeoutMs);
        _heartbeat = new HeartbeatMonitor(settings, SendPingAsync);

        _receiveLoop.FrameReceived += OnFrameReceived;
        _receiveLoop.ProtocolViolation += (code, _) => CloseOnce($"PROTOCOL_VIOLATION:{code}");
        _receiveLoop.Disconnected += () => CloseOnce("DISCONNECTED");
        _receiveLoop.TransportTimedOut += () => CloseOnce("TRANSPORT_TIMEOUT");
        _heartbeat.TimedOut += () => CloseOnce("HEARTBEAT_TIMEOUT");
    }

    /// <summary>Bắt đầu nhận frame + heartbeat. Gọi 1 lần sau khi đã đăng ký FrameReceived/Closed.</summary>
    public void Start()
    {
        if (_receiveLoopTask is not null) throw new InvalidOperationException("HeartbeatedConnection đã Start rồi.");
        _receiveLoopTask = RunReceiveLoopAsync();
        _heartbeat.Start();
    }

    private async Task RunReceiveLoopAsync()
    {
        try
        {
            await _receiveLoop.RunAsync(_cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Bị huỷ chủ động từ CloseOnce (vd. heartbeat timeout) — không phải lỗi.
        }
        catch (Exception ex)
        {
            // P2-TV1-D4: lỗi KHÔNG LƯỜNG TRƯỚC ở tầng transport (vd. socket bị Server
            // đóng đột ngột theo kiểu RST thay vì FIN sạch, gây IOException thay vì EOF
            // bình thường). TUYỆT ĐỐI không được để văng thành unobserved task exception
            // — bắt tại đây, báo ra ngoài qua Faulted, rồi vẫn đóng kết nối graceful.
            Faulted?.Invoke(ex);
            CloseOnce($"UNEXPECTED_ERROR:{ex.GetType().Name}");
        }
    }

    private void OnFrameReceived(byte[] raw, string json)
    {
        _heartbeat.NotifyActivity();

        if (_rateLimiter is not null && !_rateLimiter.TryConsume())
        {
            int violations = Interlocked.Increment(ref _consecutiveRateViolations);
            RateLimited?.Invoke();

            if (violations >= _maxViolationsBeforeClose)
            {
                CloseOnce("RATE_LIMIT_EXCEEDED");
            }

            return; // Frame vượt hạn mức bị drop — KHÔNG forward lên tầng ứng dụng.
        }

        Interlocked.Exchange(ref _consecutiveRateViolations, 0); // vi phạm KHÔNG liên tục -> không cộng dồn tới ngưỡng đóng

        // Lọc PING/PONG bằng đúng field "type" (không phải substring-match trên cả
        // JSON — tránh lọc nhầm 1 tin nhắn nghiệp vụ hợp lệ chỉ vì nội dung chứa
        // chữ "PING"/"PONG" ở đâu đó, ví dụ payload chat).
        if (TryGetMessageType(json) is HeartbeatMessageTypes.Ping or HeartbeatMessageTypes.Pong)
        {
            return;
        }

        FrameReceived?.Invoke(raw, json);
    }

    private static string? TryGetMessageType(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("type", out var typeProp) && typeProp.ValueKind == System.Text.Json.JsonValueKind.String
                ? typeProp.GetString()
                : null;
        }
        catch (System.Text.Json.JsonException)
        {
            // Không nên xảy ra (ConnectionReceiveLoop đã validate JSON well-formed trước khi
            // raise FrameReceived), nhưng phòng thủ: coi như không phải heartbeat, để tầng
            // ứng dụng tự quyết định xử lý tiếp.
            return null;
        }
    }

    private Task SendPingAsync(CancellationToken ct)
    {
        var payload = Encoding.UTF8.GetBytes($"{{\"type\":\"{HeartbeatMessageTypes.Ping}\"}}");
        return SendFrameAsync(payload, ct);
    }

    /// <summary>
    /// Gửi 1 frame nghiệp vụ. Dùng chung write-lock với PING nội bộ — an toàn gọi
    /// đồng thời. P2-TV1-D4: token huỷ được LIÊN KẾT với vòng đời connection — nếu
    /// connection đã đóng (bất kỳ lý do gì), lệnh gửi đang chờ (hoặc gọi sau đó) sẽ
    /// thất bại nhanh bằng OperationCanceledException thay vì treo vô thời hạn hoặc
    /// ném lỗi khó hiểu từ 1 stream đã chết — đúng tinh thần "cancellation propagation".
    /// </summary>
    public async Task SendFrameAsync(byte[] payload, CancellationToken ct = default)
    {
        if (Volatile.Read(ref _disposed) == 1)
            throw new ObjectDisposedException(nameof(HeartbeatedConnection));

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
        await _writeLock.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            await TcpFrameCodec.WriteFrameAsync(_stream, payload, linked.Token).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private void CloseOnce(string reason)
    {
        if (Interlocked.Exchange(ref _closed, 1) == 0)
        {
            _cts.Cancel();
            Closed?.Invoke(reason);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return; // đã dispose rồi — an toàn gọi lại nhiều lần, không làm gì thêm

        CloseOnce("DISPOSED");

        if (_receiveLoopTask is not null)
        {
            try { await _receiveLoopTask.ConfigureAwait(false); }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine($"Receive loop đã đóng với lỗi: {exception.Message}");
            }
        }

        await _heartbeat.DisposeAsync().ConfigureAwait(false);
        _writeLock.Dispose();
        _cts.Dispose();
    }
}
