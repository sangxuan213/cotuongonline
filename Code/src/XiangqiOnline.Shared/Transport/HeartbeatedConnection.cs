using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using XiangqiOnline.Shared.Protocol;

namespace XiangqiOnline.Shared.Transport
{
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
    /// </summary>
    public sealed class HeartbeatedConnection : IAsyncDisposable
    {
        private readonly Stream _stream;
        private readonly ConnectionReceiveLoop _receiveLoop;
        private readonly HeartbeatMonitor _heartbeat;
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private readonly CancellationTokenSource _cts = new();
        private Task? _receiveLoopTask;
        private int _closed;   // guard chống Closed event raise 2 lần
        private int _disposed; // guard chống Dispose logic chạy 2 lần (tách riêng _closed)

        /// <summary>Frame nghiệp vụ hợp lệ (KHÔNG bao gồm PING/PONG — đã bị lọc ở đây).</summary>
        public event Action<byte[], string>? FrameReceived;

        /// <summary>
        /// Raised đúng 1 lần khi kết nối đóng, dù vì lý do gì:
        /// "PROTOCOL_VIOLATION:&lt;code&gt;" | "DISCONNECTED" | "TRANSPORT_TIMEOUT" | "HEARTBEAT_TIMEOUT" | "DISPOSED".
        /// </summary>
        public event Action<string>? Closed;

        public HeartbeatedConnection(Stream stream, HeartbeatSettings settings)
        {
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
            ArgumentNullException.ThrowIfNull(settings);
            settings.Validate();

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
        }

        private void OnFrameReceived(byte[] raw, string json)
        {
            _heartbeat.NotifyActivity();

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

        /// <summary>Gửi 1 frame nghiệp vụ. Dùng chung write-lock với PING nội bộ — an toàn gọi đồng thời.</summary>
        public async Task SendFrameAsync(byte[] payload, CancellationToken ct = default)
        {
            await _writeLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await TcpFrameCodec.WriteFrameAsync(_stream, payload, ct).ConfigureAwait(false);
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
                catch { /* đã log/raise qua Closed rồi, không cần propagate lại */ }
            }

            await _heartbeat.DisposeAsync().ConfigureAwait(false);
            _writeLock.Dispose();
            _cts.Dispose();
        }
    }
}
