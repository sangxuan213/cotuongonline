using System.Text;
using System.Text.Json;
using XiangqiOnline.Shared.Protocol;

namespace XiangqiOnline.Protocol.Tests;

public sealed class ProtocolContractTests
{
    [Fact]
    public async Task Frame_round_trip_preserves_utf8_payload()
    {
        var payload = Encoding.UTF8.GetBytes("{\"type\":\"PING\",\"text\":\"Cờ tướng\"}");
        using var stream = new MemoryStream();
        await TcpFrameCodec.WriteFrameAsync(stream, payload);
        stream.Position = 0;
        Assert.Equal(payload, await TcpFrameCodec.ReadFrameAsync(stream));
    }

    [Fact]
    public async Task Oversized_frame_is_rejected_before_allocation()
    {
        using var stream = new MemoryStream(new byte[] { 0, 1, 0, 1 });
        await Assert.ThrowsAsync<FrameDecodeException>(() => TcpFrameCodec.ReadFrameAsync(stream));
    }

    [Fact]
    public void Request_envelope_uses_protocol_version_one()
    {
        using var json = JsonDocument.Parse("""{"protocolVersion":"1.0","type":"HELLO","requestId":"r1","clientSequence":1,"sentAtUtc":"2026-01-01T00:00:00Z","payload":{}}""");
        Assert.Equal(ProtocolConstants.ProtocolVersion, json.RootElement.GetProperty("protocolVersion").GetString());
        Assert.Equal("HELLO", json.RootElement.GetProperty("type").GetString());
    }
}
