using System;
using System.Threading;
using System.Threading.Tasks;
using XiangqiOnline.Shared.Protocol;

namespace XiangqiOnline.Server.Networking
{
    /// <summary>
    /// HELLO route: replies with HELLO_ACK carrying the supported protocol version,
    /// exactly as the locked §10.4 ServerEventEnvelope requires. The client uses
    /// payload.supportedVersion to decide whether it can continue the handshake.
    /// </summary>
    public static class HelloMessageHandler
    {
        public static async Task HandleAsync(
            RequestEnvelope<System.Text.Json.JsonElement> request,
            ClientConnectionHandler connection,
            CancellationToken ct)
        {
            var ack = new ServerEventEnvelope<object>
            {
                Type = "HELLO_ACK",
                EventId = Guid.NewGuid().ToString("N"),
                CausationRequestId = request.RequestId,
                ServerTimeUtc = DateTimeOffset.UtcNow,
                Payload = new { supportedVersion = ProtocolConstants.ProtocolVersion }
            };

            await connection.SendAsync(ack, ct).ConfigureAwait(false);
        }
    }
}