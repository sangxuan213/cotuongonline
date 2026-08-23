using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using XiangqiOnline.Shared.Protocol;

namespace XiangqiOnline.Shared.Transport
{
    /// <summary>
    /// Continuously pulls frames off a Stream using TcpFrameCodec, validates strict
    /// UTF-8 and JSON well-formedness, and raises events. Does NOT deserialize into
    /// RequestEnvelope/ServerEventEnvelope or route messages — schema-level validation
    /// (§INVALID_MESSAGE_SCHEMA) and routing belong to the message-handling layer, out
    /// of TV1's Connection & Session boundary.
    ///
    /// Handles both fragmentation (a frame split across many small socket reads — see
    /// TcpFrameCodec.ReadExactlyAsync) and coalescing (several frames arriving in one
    /// underlying read) transparently, because each loop iteration asks for exactly one
    /// frame regardless of how the bytes happened to arrive on the wire.
    ///
    /// On any protocol violation (bad framing, invalid UTF-8, invalid JSON) the loop
    /// raises ProtocolViolation once and stops — per Technical Contract, these are hard
    /// errors the caller must respond to by closing the connection, not by resuming.
    /// </summary>
    public sealed class ConnectionReceiveLoop
    {
        // Rejects invalid byte sequences instead of silently replacing them with U+FFFD —
        // required so INVALID_UTF8 is actually detectable.
        private static readonly Encoding StrictUtf8 =
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        private readonly Stream _stream;
        private readonly int? _transportReadTimeoutMs;

        /// <summary>Raised once per validated frame: raw payload bytes + the already UTF-8/JSON-checked text.</summary>
        public event Action<byte[], string>? FrameReceived;

        /// <summary>Raised once, then the loop stops. errorCode matches Technical Contract Appendix A.</summary>
        public event Action<string, string>? ProtocolViolation;

        /// <summary>Raised on a clean disconnect (EOF between frames) — not a violation.</summary>
        public event Action? Disconnected;

        /// <summary>
        /// P2-TV1-D1: raised once when no COMPLETE frame arrives within transportReadTimeoutMs —
        /// tầng transport, độc lập với HeartbeatMonitor (tầng nghiệp vụ). Không phải
        /// ProtocolViolation (không phải lỗi của dữ liệu gửi tới) và không phải
        /// Disconnected (không có EOF sạch) — là "im lặng bất thường ở tầng socket".
        /// </summary>
        public event Action? TransportTimedOut;

        public ConnectionReceiveLoop(Stream stream, int? transportReadTimeoutMs = null)
        {
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
            _transportReadTimeoutMs = transportReadTimeoutMs;
        }

        /// <summary>
        /// Runs until the connection closes, a protocol violation occurs, or ct is cancelled.
        /// Never throws for protocol-level problems — those surface via ProtocolViolation.
        /// Only cancellation propagates as OperationCanceledException.
        /// </summary>
        public async Task RunAsync(CancellationToken ct = default)
        {
            while (!ct.IsCancellationRequested)
            {
                byte[]? payload;
                try
                {
                    payload = await ReadFrameWithTransportTimeoutAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw; // caller asked us to stop — not a protocol violation
                }
                catch (TransportTimeoutSignal)
                {
                    TransportTimedOut?.Invoke();
                    return;
                }
                catch (FrameDecodeException ex)
                {
                    ProtocolViolation?.Invoke(ex.ErrorCode, ex.Message);
                    return;
                }

                if (payload is null)
                {
                    Disconnected?.Invoke();
                    return; // clean EOF between frames
                }

                string text;
                try
                {
                    text = StrictUtf8.GetString(payload);
                }
                catch (DecoderFallbackException)
                {
                    ProtocolViolation?.Invoke("INVALID_UTF8", "Payload không giải mã được UTF-8.");
                    return;
                }

                try
                {
                    // Structural check only — well-formed JSON. Envelope schema/field
                    // validation happens in the message-handling layer, not here.
                    using var doc = JsonDocument.Parse(text);
                }
                catch (JsonException ex)
                {
                    ProtocolViolation?.Invoke("INVALID_JSON", $"JSON không hợp lệ: {ex.Message}");
                    return;
                }

                FrameReceived?.Invoke(payload, text);
            }

            ct.ThrowIfCancellationRequested();
        }

        /// <summary>
        /// Đọc 1 frame với timeout tầng transport (nếu được cấu hình). Timeout ở đây là
        /// COARSE — tính trên cả lần đọc trọn 1 frame (kể cả khi frame đó đang tới rất
        /// chậm qua nhiều lần đọc nhỏ), không phải per-byte. Đủ để bắt "socket im lặng
        /// hoàn toàn" (dây mạng đứt, NAT âm thầm drop), là lưới an toàn cuối — phát hiện
        /// "còn sống về nghiệp vụ hay không" vẫn nên dựa vào HeartbeatMonitor.
        /// </summary>
        private async Task<byte[]?> ReadFrameWithTransportTimeoutAsync(CancellationToken ct)
        {
            if (_transportReadTimeoutMs is not int timeoutMs)
                return await TcpFrameCodec.ReadFrameAsync(_stream, ct).ConfigureAwait(false);

            using var timeoutCts = new CancellationTokenSource(timeoutMs);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
            try
            {
                return await TcpFrameCodec.ReadFrameAsync(_stream, linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                throw new TransportTimeoutSignal();
            }
        }

        /// <summary>Tín hiệu nội bộ, không bao giờ lộ ra ngoài RunAsync (đã bắt và chuyển thành event).</summary>
        private sealed class TransportTimeoutSignal : Exception
        {
        }
    }
}
