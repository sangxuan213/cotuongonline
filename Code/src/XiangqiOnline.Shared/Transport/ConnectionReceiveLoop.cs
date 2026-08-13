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

        /// <summary>Raised once per validated frame: raw payload bytes + the already UTF-8/JSON-checked text.</summary>
        public event Action<byte[], string>? FrameReceived;

        /// <summary>Raised once, then the loop stops. errorCode matches Technical Contract Appendix A.</summary>
        public event Action<string, string>? ProtocolViolation;

        /// <summary>Raised on a clean disconnect (EOF between frames) — not a violation.</summary>
        public event Action? Disconnected;

        public ConnectionReceiveLoop(Stream stream)
        {
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
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
                    payload = await TcpFrameCodec.ReadFrameAsync(_stream, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw; // caller asked us to stop — not a protocol violation
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
    }
}
