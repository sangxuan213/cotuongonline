using System.Text.Json;
using XiangqiOnline.Shared.Protocol;

namespace XiangqiOnline.Shared.Tests.Protocol;

public sealed class ProtocolEnvelopePhase4Tests
{
    [Fact]
    public void ProtocolVersion_RemainsLockedAtOnePointZero() =>
        Assert.Equal("1.0", ProtocolConstants.ProtocolVersion);

    [Fact]
    public void RequestEnvelope_SerializesAllLockedFields()
    {
        var request = new RequestEnvelope<object>
        {
            Type = "PING", RequestId = "request-1", SessionToken = "secret", RoomId = "room-1",
            ClientSequence = 9, SentAtUtc = DateTimeOffset.UnixEpoch, Payload = new { nonce = "n" }
        };
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(request));

        foreach (var name in new[] { "protocolVersion", "type", "requestId", "sessionToken", "roomId", "clientSequence", "sentAtUtc", "payload" })
            Assert.True(json.RootElement.TryGetProperty(name, out _), $"Missing field {name}");
    }

    [Fact]
    public void ServerEventEnvelope_SerializesRevisionAndCausation()
    {
        var message = new ServerEventEnvelope<object>
        {
            Type = "MOVE_COMMITTED", EventId = "event-1", CausationRequestId = "request-1",
            RoomId = "room-1", Revision = 2, ServerSequence = 3, ServerTimeUtc = DateTimeOffset.UnixEpoch,
            Payload = new { }
        };
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(message));

        Assert.Equal(2, json.RootElement.GetProperty("revision").GetInt64());
        Assert.Equal("request-1", json.RootElement.GetProperty("causationRequestId").GetString());
    }

    [Fact]
    public void EnvelopeDeserialization_UsesDefaultProtocolForMissingVersion()
    {
        var request = JsonSerializer.Deserialize<RequestEnvelope<JsonElement>>("{\"type\":\"PING\",\"requestId\":\"r\",\"payload\":{}}")!;
        Assert.Equal("1.0", request.ProtocolVersion);
    }
}
