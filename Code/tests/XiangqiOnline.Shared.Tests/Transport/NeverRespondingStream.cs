using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace XiangqiOnline.Shared.Tests.Transport
{
    /// <summary>
    /// ReadAsync không bao giờ tự hoàn thành — chỉ kết thúc khi CancellationToken bị
    /// hủy (đúng hành vi của 1 socket read thật khi dây mạng "câm" hoàn toàn, không có
    /// byte nào tới và cũng không có FIN/RST). Dùng để test transportReadTimeoutMs.
    /// </summary>
    public sealed class NeverRespondingStream : Stream
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            await using (ct.Register(() => tcs.TrySetCanceled(ct)))
            {
                return await tcs.Task.ConfigureAwait(false);
            }
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
}
