using System.Text.Json;
using XiangqiOnline.Server.Lobby;
using XiangqiOnline.Shared.Models;
using XiangqiOnline.Shared.Protocol;

namespace XiangqiOnline.Server.Networking
{
    public static class AcceptChallengeMessageHandler
    {
        public static async Task HandleAsync(
            RequestEnvelope<JsonElement> request,
            ClientConnectionHandler connection,
            PlayerSessionDirectory directory,
            ChallengeManager challenges,
            IConnectionRegistry connections,
            CancellationToken ct)
        {
            if (!directory.TryGetByConnectionId(connection.ConnectionId, out var accepter))
            {
                await connection.SendErrorAsync(ErrorCodes.INVALID_SESSION, "Accepter is not logged in.", request.RequestId, ct).ConfigureAwait(false);
                return;
            }

            var value = request.Payload;
            if (!value.TryGetProperty("challengeId", out var challengeIdNode) ||
                string.IsNullOrWhiteSpace(challengeIdNode.GetString()))
            {
                await connection.SendErrorAsync(ErrorCodes.CHALLENGE_NOT_FOUND, "CHALLENGE_ACCEPT requires 'challengeId'.", request.RequestId, ct).ConfigureAwait(false);
                return;
            }

            var result = challenges.AcceptChallenge(challengeIdNode.GetString()!, accepter.PlayerId, DateTimeOffset.UtcNow);
            if (!result.IsSuccess)
            {
                await connection.SendErrorAsync(result.ErrorCode ?? ErrorCodes.CHALLENGE_NOT_FOUND, result.Message, request.RequestId, ct).ConfigureAwait(false);
                return;
            }

            var room = result.Room!;
            var roomCreated = RoomMessages.RoomCreated(room, request.RequestId);
            var snapshot = RoomMessages.GameStateSnapshot(room, request.RequestId);
            var playersToNotify = new[]
            {
                directory.TryGetByPlayerId(room.RedPlayerId, out var red) ? red : null,
                directory.TryGetByPlayerId(room.BlackPlayerId, out var black) ? black : null
            };

            foreach (var player in playersToNotify)
            {
                if (player is null || !connections.TryGetConnection(player.ConnectionId, out var playerConnection))
                    continue;

                await playerConnection.SendAsync(roomCreated, ct).ConfigureAwait(false);
                await playerConnection.SendAsync(snapshot, ct).ConfigureAwait(false);
            }
        }
    }
}
