using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using XiangqiOnline.Shared.Protocol;
using XiangqiOnline.Shared.Transport;
using Xunit;

namespace XiangqiOnline.Shared.Tests.Transport;

public class ConnectionReceiveLoopTests
{
    private static byte[] BuildFrame(string json)
    {
        using var ms = new MemoryStream();
        TcpFrameCodec.WriteFrameAsync(ms, Encoding.UTF8.GetBytes(json)).GetAwaiter().GetResult();
        return ms.ToArray();
    }

    [Fact]
    public async Task RunAsync_FragmentedStream_StillReceivesFrameCorrectly()
    {
        // 1 byte at a time — worst-case fragmentation. If ReadExactlyAsync were
        // buggy (e.g. assumed one ReadAsync = one full frame), this would fail.
        var frameBytes = BuildFrame("{\"type\":\"HELLO\"}");
        var stream = new ChunkedStream(frameBytes, chunkSize: 1);
        var loop = new ConnectionReceiveLoop(stream);

        string? received = null;
        loop.FrameReceived += (_, json) => received = json;

        await loop.RunAsync();

        Assert.Equal("{\"type\":\"HELLO\"}", received);
    }

    [Fact]
    public async Task RunAsync_TwoFramesCoalescedInOneChunk_FiresFrameReceivedTwice()
    {
        var first = BuildFrame("{\"type\":\"HELLO\"}");
        var second = BuildFrame("{\"type\":\"PING\"}");
        var combined = new byte[first.Length + second.Length];
        first.CopyTo(combined, 0);
        second.CopyTo(combined, first.Length);

        // chunkSize larger than the whole payload -> both frames can arrive in one ReadAsync
        var stream = new ChunkedStream(combined, chunkSize: combined.Length);
        var loop = new ConnectionReceiveLoop(stream);

        var receivedFrames = new System.Collections.Generic.List<string>();
        loop.FrameReceived += (_, json) => receivedFrames.Add(json);

        await loop.RunAsync();

        Assert.Equal(2, receivedFrames.Count);
        Assert.Equal("{\"type\":\"HELLO\"}", receivedFrames[0]);
        Assert.Equal("{\"type\":\"PING\"}", receivedFrames[1]);
    }

    [Fact]
    public async Task RunAsync_InvalidUtf8Payload_RaisesProtocolViolation_INVALID_UTF8()
    {
        // 0xFF 0xFE is not valid UTF-8.
        using var ms = new MemoryStream();
        await TcpFrameCodec.WriteFrameAsync(ms, new byte[] { 0xFF, 0xFE, 0x00, 0x01 });
        ms.Position = 0;

        var loop = new ConnectionReceiveLoop(ms);
        string? code = null;
        loop.ProtocolViolation += (errorCode, _) => code = errorCode;

        await loop.RunAsync();

        Assert.Equal("INVALID_UTF8", code);
    }

    [Fact]
    public async Task RunAsync_MalformedJson_RaisesProtocolViolation_INVALID_JSON()
    {
        using var ms = new MemoryStream();
        await TcpFrameCodec.WriteFrameAsync(ms, Encoding.UTF8.GetBytes("{not valid json"));
        ms.Position = 0;

        var loop = new ConnectionReceiveLoop(ms);
        string? code = null;
        loop.ProtocolViolation += (errorCode, _) => code = errorCode;

        await loop.RunAsync();

        Assert.Equal("INVALID_JSON", code);
    }

    [Fact]
    public async Task RunAsync_InvalidFrameLength_RaisesProtocolViolation_INVALID_FRAME_LENGTH()
    {
        using var ms = new MemoryStream(new byte[] { 0, 0, 0, 0 }); // length = 0
        var loop = new ConnectionReceiveLoop(ms);
        string? code = null;
        loop.ProtocolViolation += (errorCode, _) => code = errorCode;

        await loop.RunAsync();

        Assert.Equal("INVALID_FRAME_LENGTH", code);
    }

    [Fact]
    public async Task RunAsync_CleanEofBetweenFrames_RaisesDisconnected_NotViolation()
    {
        var frameBytes = BuildFrame("{\"type\":\"HELLO\"}");
        using var ms = new MemoryStream(frameBytes); // exactly one frame, then EOF
        var loop = new ConnectionReceiveLoop(ms);

        bool disconnected = false;
        bool violated = false;
        loop.FrameReceived += (_, _) => { };
        loop.Disconnected += () => disconnected = true;
        loop.ProtocolViolation += (_, _) => violated = true;

        await loop.RunAsync();

        Assert.True(disconnected);
        Assert.False(violated);
    }

    [Fact]
    public async Task RunAsync_AfterProtocolViolation_StopsLoop_DoesNotProcessFurtherBytes()
    {
        // A bad frame followed by a well-formed one — loop must stop at the first
        // violation and never touch the second frame.
        using var ms = new MemoryStream();
        await TcpFrameCodec.WriteFrameAsync(ms, Encoding.UTF8.GetBytes("{bad json"));
        await TcpFrameCodec.WriteFrameAsync(ms, Encoding.UTF8.GetBytes("{\"type\":\"PING\"}"));
        ms.Position = 0;

        var loop = new ConnectionReceiveLoop(ms);
        int violationCount = 0;
        int frameCount = 0;
        loop.ProtocolViolation += (_, _) => violationCount++;
        loop.FrameReceived += (_, _) => frameCount++;

        await loop.RunAsync();

        Assert.Equal(1, violationCount);
        Assert.Equal(0, frameCount);
    }
}
