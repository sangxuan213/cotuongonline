using System;
using System.Text.Json.Serialization;

namespace XiangqiOnline.Shared.Protocol
{
    public static class ProtocolConstants
    {
        public const string ProtocolVersion = "1.0";
    }

    /// <summary>
    /// Client -> Server request envelope. Field set and names are locked per
    /// Technical Contract §10.3 — do not rename/remove without a Change Request.
    /// </summary>
    public class RequestEnvelope<TPayload>
    {
        [JsonPropertyName("protocolVersion")]
        public string ProtocolVersion { get; set; } = ProtocolConstants.ProtocolVersion;

        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("requestId")]
        public string RequestId { get; set; } = string.Empty;

        [JsonPropertyName("sessionToken")]
        public string? SessionToken { get; set; }

        [JsonPropertyName("roomId")]
        public string? RoomId { get; set; }

        [JsonPropertyName("clientSequence")]
        public long ClientSequence { get; set; }

        [JsonPropertyName("sentAtUtc")]
        public DateTimeOffset SentAtUtc { get; set; }

        [JsonPropertyName("payload")]
        public TPayload? Payload { get; set; }
    }

    /// <summary>
    /// Server -> Client event envelope. Field set and names are locked per
    /// Technical Contract §10.4 — do not rename/remove without a Change Request.
    /// </summary>
    public class ServerEventEnvelope<TPayload>
    {
        [JsonPropertyName("protocolVersion")]
        public string ProtocolVersion { get; set; } = ProtocolConstants.ProtocolVersion;

        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("eventId")]
        public string EventId { get; set; } = string.Empty;

        [JsonPropertyName("causationRequestId")]
        public string? CausationRequestId { get; set; }

        [JsonPropertyName("roomId")]
        public string? RoomId { get; set; }

        [JsonPropertyName("revision")]
        public long? Revision { get; set; }

        [JsonPropertyName("serverSequence")]
        public long ServerSequence { get; set; }

        [JsonPropertyName("serverTimeUtc")]
        public DateTimeOffset ServerTimeUtc { get; set; }

        [JsonPropertyName("payload")]
        public TPayload? Payload { get; set; }
    }
}
