using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using XiangqiOnline.Shared.Protocol;
using Xunit;

namespace XiangqiOnline.Shared.Tests.Protocol;

public class TcpFrameCodecTests
{
    [Fact]
    public async Task WriteThenRead_ValidFrame_RoundTripsExactPayload()
    {
        var payload = Encoding.UTF8.GetBytes("{\"type\":\"HELLO\"}");
        using var stream = new MemoryStream();

        await TcpFrameCodec.WriteFrameAsync(stream, payload);
        stream.Position = 0;

        var result = await TcpFrameCodec.ReadFrameAsync(stream);

        Assert.NotNull(result);
        Assert.Equal(payload, result);
    }

    [Fact]
    public async Task ReadFrameAsync_CleanEofBeforeHeader_ReturnsNull()
    {
        using var stream = new MemoryStream(); // nothing written — clean end of connection
        var result = await TcpFrameCodec.ReadFrameAsync(stream);
        Assert.Null(result);
    }

    [Fact]
    public async Task ReadFrameAsync_ZeroLength_ThrowsFrameDecodeException()
    {
        using var stream = new MemoryStream(new byte[] { 0, 0, 0, 0 });
        await Assert.ThrowsAsync<FrameDecodeException>(() => TcpFrameCodec.ReadFrameAsync(stream));
    }

    [Fact]
    public async Task ReadFrameAsync_LengthOverMax_ThrowsFrameDecodeException()
    {
        // big-endian 0x00010001 = 65,537 > 65,536 (64 KiB)
        using var stream = new MemoryStream(new byte[] { 0x00, 0x01, 0x00, 0x01 });
        await Assert.ThrowsAsync<FrameDecodeException>(() => TcpFrameCodec.ReadFrameAsync(stream));
    }

    [Fact]
    public async Task ReadFrameAsync_PartialPayload_ThrowsFrameDecodeException()
    {
        // header declares 10 bytes, only 3 are actually written -> mid-frame disconnect
        var header = new byte[] { 0x00, 0x00, 0x00, 0x0A };
        var partialPayload = new byte[] { 1, 2, 3 };
        using var stream = new MemoryStream();
        stream.Write(header);
        stream.Write(partialPayload);
        stream.Position = 0;

        await Assert.ThrowsAsync<FrameDecodeException>(() => TcpFrameCodec.ReadFrameAsync(stream));
    }

    [Fact]
    public async Task WriteFrameAsync_PayloadOverMax_ThrowsFrameEncodeException()
    {
        var oversized = new byte[TcpFrameCodec.MaxPayloadBytes + 1];
        using var stream = new MemoryStream();

        await Assert.ThrowsAsync<FrameEncodeException>(() => TcpFrameCodec.WriteFrameAsync(stream, oversized));
    }

    [Fact]
    public async Task WriteFrameAsync_EmptyPayload_ThrowsFrameEncodeException()
    {
        using var stream = new MemoryStream();
        await Assert.ThrowsAsync<FrameEncodeException>(() => TcpFrameCodec.WriteFrameAsync(stream, Array.Empty<byte>()));
    }

    [Fact]
    public async Task TwoFramesBackToBack_BothDecodeCorrectly()
    {
        // Guards against NET-03 in the risk register: two frames arriving in one read.
        var first = Encoding.UTF8.GetBytes("{\"type\":\"HELLO\"}");
        var second = Encoding.UTF8.GetBytes("{\"type\":\"PING\"}");
        using var stream = new MemoryStream();

        await TcpFrameCodec.WriteFrameAsync(stream, first);
        await TcpFrameCodec.WriteFrameAsync(stream, second);
        stream.Position = 0;

        var firstRead = await TcpFrameCodec.ReadFrameAsync(stream);
        var secondRead = await TcpFrameCodec.ReadFrameAsync(stream);

        Assert.Equal(first, firstRead);
        Assert.Equal(second, secondRead);
    }

    [Fact]
    public void RequestEnvelope_SerializesWithLockedFieldNames()
    {
        var envelope = new RequestEnvelope<object>
        {
            Type = "MOVE_REQUEST",
            RequestId = "01J-example",
            SessionToken = "token",
            RoomId = "ROOM-01J",
            ClientSequence = 42,
            SentAtUtc = DateTimeOffset.UtcNow,
            Payload = new { from = new { x = 1, y = 9 }, to = new { x = 2, y = 7 } }
        };

        var json = JsonSerializer.Serialize(envelope);

        Assert.Contains("\"protocolVersion\":\"1.0\"", json);
        Assert.Contains("\"requestId\":\"01J-example\"", json);
        Assert.Contains("\"clientSequence\":42", json);
    }
}
