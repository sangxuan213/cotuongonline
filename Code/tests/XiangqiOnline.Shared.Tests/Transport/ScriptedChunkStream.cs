using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace XiangqiOnline.Shared.Tests.Transport
{
    /// <summary>
    /// Wraps a byte buffer and returns it across a scripted sequence of ReadAsync
    /// call sizes — e.g. readSizes: [1, 3] returns 1 byte on the first call, 3 bytes
    /// on the second, then whatever is left in one shot on every call after that.
    /// Used to prove exact split points (like "header arrives as 1 byte then 3 bytes")
    /// are handled correctly, not just fragmentation in general.
    /// </summary>
    public sealed class ScriptedChunkStream : Stream
    {
        private readonly byte[] _data;
        private readonly int[] _readSizes;
        private int _position;
        private int _scriptIndex;

        public ScriptedChunkStream(byte[] data, params int[] readSizes)
        {
            _data = data;
            _readSizes = readSizes;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            await Task.Yield();
            if (_position >= _data.Length) return 0;

            int requested = _scriptIndex < _readSizes.Length
                ? _readSizes[_scriptIndex++]
                : _data.Length - _position; // script exhausted -> hand back the rest at once

            int toCopy = Math.Min(requested, Math.Min(buffer.Length, _data.Length - _position));
            _data.AsSpan(_position, toCopy).CopyTo(buffer.Span);
            _position += toCopy;
            return toCopy;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _data.Length;
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
