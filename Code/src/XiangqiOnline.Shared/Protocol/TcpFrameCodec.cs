using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace XiangqiOnline.Shared.Protocol
{
    /// <summary>
    /// Encodes/decodes TCP frames per Protocol Catalog v1.0 §10.1–10.2:
    ///   [4-byte unsigned big-endian length][UTF-8 JSON payload, exactly Length bytes]
    ///
    /// LOCKED CONTRACT — do not change LengthPrefixBytes, byte order, or MaxPayloadBytes
    /// without a Change Request (see Technical Contract §18). TV2–TV5 build on top of this.
    /// This class only handles bytes; it does not open sockets (that's TcpServerHost /
    /// TcpClientService, Day 2) and does not parse JSON (callers own that step).
    /// </summary>
    public static class TcpFrameCodec
    {
        public const int LengthPrefixBytes = 4;
        public const int MaxPayloadBytes = 64 * 1024; // 64 KiB, Protocol Catalog v1.0 §10.1

        /// <summary>
        /// Writes one frame (length prefix + payload) to the stream and flushes it.
        /// Throws FrameEncodeException if payload is empty or exceeds MaxPayloadBytes.
        /// </summary>
        public static async Task WriteFrameAsync(Stream stream, ReadOnlyMemory<byte> payload, CancellationToken ct = default)
        {
            if (payload.Length == 0)
                throw new FrameEncodeException("Payload rỗng không hợp lệ (length = 0).");
            if (payload.Length > MaxPayloadBytes)
                throw new FrameEncodeException($"Payload {payload.Length} byte vượt giới hạn {MaxPayloadBytes} byte.");

            var header = new byte[LengthPrefixBytes];
            BinaryPrimitives.WriteUInt32BigEndian(header, (uint)payload.Length);

            await stream.WriteAsync(header, ct).ConfigureAwait(false);
            await stream.WriteAsync(payload, ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Reads exactly one frame from the stream.
        /// Returns null on a clean disconnect that happens *before* any header byte arrives
        /// (normal end of connection between frames).
        /// Throws FrameDecodeException for: invalid length (0 or &gt; MaxPayloadBytes, i.e.
        /// INVALID_FRAME_LENGTH), or the socket closing mid-frame (partial header/payload).
        /// </summary>
        public static async Task<byte[]?> ReadFrameAsync(Stream stream, CancellationToken ct = default)
        {
            var header = new byte[LengthPrefixBytes];
            int headerRead = await ReadExactlyAsync(stream, header, ct).ConfigureAwait(false);
            if (headerRead == 0)
                return null; // clean disconnect before next frame

            uint length = BinaryPrimitives.ReadUInt32BigEndian(header);
            if (length == 0 || length > MaxPayloadBytes)
                throw new FrameDecodeException($"INVALID_FRAME_LENGTH: length={length}");

            var payload = new byte[length];
            int payloadRead = await ReadExactlyAsync(stream, payload, ct).ConfigureAwait(false);
            if (payloadRead != payload.Length)
                throw new FrameDecodeException("Kết nối đóng giữa frame (payload không đủ byte).");

            return payload;
        }

        /// <summary>
        /// Fills buffer completely, or returns 0 if EOF hits before any byte is read.
        /// Throws FrameDecodeException if EOF hits mid-buffer (partial frame — a bug or
        /// an abrupt disconnect the caller must treat as a fault, not silent data loss).
        /// </summary>
        private static async Task<int> ReadExactlyAsync(Stream stream, Memory<byte> buffer, CancellationToken ct)
        {
            int totalRead = 0;
            while (totalRead < buffer.Length)
            {
                int read = await stream.ReadAsync(buffer[totalRead..], ct).ConfigureAwait(false);
                if (read == 0)
                {
                    if (totalRead == 0) return 0;
                    throw new FrameDecodeException("Kết nối đóng giữa frame.");
                }
                totalRead += read;
            }
            return totalRead;
        }
    }

    public class FrameDecodeException : Exception
    {
        public FrameDecodeException(string message) : base(message) { }
    }

    public class FrameEncodeException : Exception
    {
        public FrameEncodeException(string message) : base(message) { }
    }
}
