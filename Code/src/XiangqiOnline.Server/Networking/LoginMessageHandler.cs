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
        public static Task SendLoginResultAsync(
            string displayName,
            string causationRequestId,
            ClientConnectionHandler connection,
            PlayerSessionDirectory directory,
            CancellationToken ct,
            string? stablePlayerId = null)
            => LoginCoreAsync(displayName, causationRequestId, connection, directory, ct, stablePlayerId);

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

            await LoginCoreAsync(displayName ?? string.Empty, request.RequestId, connection, directory, ct, null).ConfigureAwait(false);
        }

        private static async Task LoginCoreAsync(
            string displayName,
            string causationRequestId,
            ClientConnectionHandler connection,
            PlayerSessionDirectory directory,
            CancellationToken ct,
            string? stablePlayerId)
        {
            var result = directory.Login(displayName, connection.ConnectionId, DateTimeOffset.UtcNow, stablePlayerId);
            if (!result.IsSuccess)
            {
                await connection.SendAsync(new ServerEventEnvelope<object>
                {
                    Type = "LOGIN_RESULT",
                    EventId = Guid.NewGuid().ToString("N"),
                    CausationRequestId = causationRequestId,
                    ServerTimeUtc = DateTimeOffset.UtcNow,
                    Payload = new { status = "REJECTED", errorCode = result.ErrorCode ?? "LOGIN_REJECTED", message = result.Message }
                }, ct).ConfigureAwait(false);
                return;
            }

            var session = result.Session!;
            var envelope = new ServerEventEnvelope<object>
            {
                Type = "LOGIN_RESULT",
                EventId = Guid.NewGuid().ToString("N"),
                CausationRequestId = causationRequestId,
                ServerTimeUtc = DateTimeOffset.UtcNow,
                Payload = new
                {
                    status = "ACCEPTED",
                    token = result.SessionToken,
                    player = new { playerId = session.PlayerId, displayName = session.DisplayName }
                }
            };

            await connection.SendAsync(envelope, ct).ConfigureAwait(false);
        }
    }
}
