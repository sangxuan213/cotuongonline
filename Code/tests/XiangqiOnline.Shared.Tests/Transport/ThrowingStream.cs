using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace XiangqiOnline.Shared.Tests.Transport;

/// <summary>
/// ReadAsync trả về vài byte hợp lệ đầu tiên (đủ để bắt đầu 1 frame), rồi ném 1
/// exception KHÔNG PHẢI OperationCanceledException/FrameDecodeException — mô phỏng
/// lỗi tầng socket thật (vd. connection reset, "An existing connection was forcibly
/// closed by the remote host") mà .NET đôi khi ném ra thay vì trả EOF sạch.
/// </summary>
public sealed class ThrowingStream : Stream
{
    private int _readCount;

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        _readCount++;
        if (_readCount > 2)
            throw new IOException("Đã ngắt kết nối bất thường (giả lập).");

        // 2 byte đầu của length header rồi "treo" — lần đọc thứ 3 mới ném lỗi.
        buffer.Span[0] = 0;
        return ValueTask.FromResult(1);
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
