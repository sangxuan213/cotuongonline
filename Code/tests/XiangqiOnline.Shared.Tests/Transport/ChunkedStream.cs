using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace XiangqiOnline.Shared.Tests.Transport;

/// <summary>
/// Wraps a byte buffer and hands it out a few bytes at a time, regardless of how
/// much the caller asked for — simulating a real TCP socket where one ReadAsync
/// call is NOT guaranteed to return a whole frame (or even a whole length prefix).
/// A plain MemoryStream would return everything in one call and wouldn't catch
/// fragmentation bugs; this class exists specifically to catch them.
/// </summary>
public sealed class ChunkedStream : Stream
{
    private readonly byte[] _data;
    private readonly int _chunkSize;
    private int _position;

    public ChunkedStream(byte[] data, int chunkSize = 1)
    {
        _data = data;
        _chunkSize = chunkSize;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        await Task.Yield(); // force this to behave like real async I/O, not a sync shortcut
        if (_position >= _data.Length) return 0;

        int toCopy = Math.Min(_chunkSize, Math.Min(buffer.Length, _data.Length - _position));
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
