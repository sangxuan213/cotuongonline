using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using XiangqiOnline.Server.Lobby;
using XiangqiOnline.Shared.Protocol;

namespace XiangqiOnline.Server.Networking
{
    /// <summary>
    /// LOGIN_REQUEST route: accepts displayName, registers the connection in
    /// PlayerSessionDirectory, and replies LOGIN_RESULT exactly as the client
    /// GameClient.ParseLogin expects (status ACCEPTED, token, player).
    /// </summary>
    public static class LoginMessageHandler
    {
        public static async Task HandleAsync(
            RequestEnvelope<JsonElement> request,
            ClientConnectionHandler connection,
            PlayerSessionDirectory directory,
            CancellationToken ct)
        {
            string? displayName = null;
            if (request.Payload is { } payload &&
                payload.TryGetProperty("displayName", out var nameNode))
            {
                displayName = nameNode.GetString();
            }

            var result = directory.Login(displayName ?? string.Empty, connection.ConnectionId, DateTimeOffset.UtcNow);
            if (!result.IsSuccess)
            {
                await connection.SendErrorAsync(
                    result.ErrorCode ?? "LOGIN_REJECTED",
                    result.Message,
                    request.RequestId,
                    ct).ConfigureAwait(false);
                return;
            }

            var session = result.Session!;
            var envelope = new ServerEventEnvelope<object>
            {
                Type = "LOGIN_RESULT",
                EventId = Guid.NewGuid().ToString("N"),
                CausationRequestId = request.RequestId,
                ServerTimeUtc = DateTimeOffset.UtcNow,
                Payload = new
                {
                    status = "ACCEPTED",
                    token = session.PlayerId,
                    player = new { playerId = session.PlayerId, displayName = session.DisplayName }
                }
            };

            await connection.SendAsync(envelope, ct).ConfigureAwait(false);
        }
    }
}
