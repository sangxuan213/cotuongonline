using System.IO;
using System.Text;
using System.Threading.Tasks;
using XiangqiOnline.Shared.Protocol;
using XiangqiOnline.Shared.Transport;
using Xunit;

namespace XiangqiOnline.Shared.Tests.Transport
{
    public class ConnectionReceiveLoopTransportTimeoutTests
    {
        [Fact]
        public async Task RunAsync_NoDataArrivesWithinTransportTimeout_RaisesTransportTimedOut()
        {
            var stream = new NeverRespondingStream();
            var loop = new ConnectionReceiveLoop(stream, transportReadTimeoutMs: 100);

            bool transportTimedOut = false;
            bool violation = false;
            bool disconnected = false;
            loop.TransportTimedOut += () => transportTimedOut = true;
            loop.ProtocolViolation += (_, _) => violation = true;
            loop.Disconnected += () => disconnected = true;

            await loop.RunAsync();

            Assert.True(transportTimedOut);
            Assert.False(violation);
            Assert.False(disconnected);
        }

        [Fact]
        public async Task RunAsync_FrameArrivesWithinTransportTimeout_DoesNotRaiseTransportTimedOut()
        {
            using var ms = new MemoryStream();
            await TcpFrameCodec.WriteFrameAsync(ms, Encoding.UTF8.GetBytes("{\"type\":\"HELLO\"}"));
            ms.Position = 0;

            // Timeout rộng rãi so với MemoryStream (trả dữ liệu ngay lập tức) -> không bao giờ chạm timeout.
            var loop = new ConnectionReceiveLoop(ms, transportReadTimeoutMs: 5000);

            bool transportTimedOut = false;
            string? received = null;
            loop.TransportTimedOut += () => transportTimedOut = true;
            loop.FrameReceived += (_, json) => received = json;

            await loop.RunAsync();

            Assert.False(transportTimedOut);
            Assert.Equal("{\"type\":\"HELLO\"}", received);
        }

        [Fact]
        public async Task RunAsync_TransportTimeoutNotConfigured_NeverRaisesTransportTimedOut_ExistingBehaviorUnchanged()
        {
            // Không truyền transportReadTimeoutMs -> hành vi giống hệt Ngày 1-5 (không có event mới nào bắn ra).
            var frameBytes = new MemoryStream();
            await TcpFrameCodec.WriteFrameAsync(frameBytes, Encoding.UTF8.GetBytes("{\"type\":\"PING\"}"));
            frameBytes.Position = 0;

            var loop = new ConnectionReceiveLoop(frameBytes);
            bool transportTimedOut = false;
            loop.TransportTimedOut += () => transportTimedOut = true;

            await loop.RunAsync();

            Assert.False(transportTimedOut);
        }
    }
}
