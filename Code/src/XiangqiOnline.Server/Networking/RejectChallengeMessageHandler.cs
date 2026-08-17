using System.Text.Json;
using XiangqiOnline.Server.Lobby;
using XiangqiOnline.Shared.Models;
using XiangqiOnline.Shared.Protocol;

namespace XiangqiOnline.Server.Networking
{
    public static class RejectChallengeMessageHandler
    {
        public static async Task HandleAsync(
            RequestEnvelope<JsonElement> request,
            ClientConnectionHandler connection,
            PlayerSessionDirectory directory,
            ChallengeManager challenges,
            IConnectionRegistry connections,
            CancellationToken ct)
        {
            if (!directory.TryGetByConnectionId(connection.ConnectionId, out var rejecter))
            {
                await connection.SendErrorAsync(ErrorCodes.INVALID_SESSION, "Rejecter is not logged in.", request.RequestId, ct).ConfigureAwait(false);
                return;
            }

            var value = request.Payload;
            if (!value.TryGetProperty("challengeId", out var challengeIdNode) ||
                string.IsNullOrWhiteSpace(challengeIdNode.GetString()))
            {
                await connection.SendErrorAsync(ErrorCodes.CHALLENGE_NOT_FOUND, "CHALLENGE_REJECT requires 'challengeId'.", request.RequestId, ct).ConfigureAwait(false);
                return;
            }

            var result = challenges.RejectChallenge(challengeIdNode.GetString()!, rejecter.PlayerId, DateTimeOffset.UtcNow);
            if (!result.IsSuccess)
            {
                await connection.SendErrorAsync(result.ErrorCode ?? ErrorCodes.CHALLENGE_NOT_FOUND, result.Message, request.RequestId, ct).ConfigureAwait(false);
                return;
            }

            var challenge = result.Challenge!;
            var rejectedEvent = new ServerEventEnvelope<object>
            {
                Type = "CHALLENGE_REJECTED",
                EventId = Guid.NewGuid().ToString("N"),
                CausationRequestId = request.RequestId,
                ServerTimeUtc = DateTimeOffset.UtcNow,
                Payload = new
                {
                    challengeId = challenge.ChallengeId,
                    rejectedByPlayerId = rejecter.PlayerId,
                    status = challenge.Status.ToString()
                }
            };
            var playersToNotify = new[]
            {
                directory.TryGetByPlayerId(challenge.ChallengerPlayerId, out var challenger) ? challenger : null,
                directory.TryGetByPlayerId(challenge.TargetPlayerId, out var target) ? target : null
            };

            foreach (var player in playersToNotify)
            {
                if (player is not null && connections.TryGetConnection(player.ConnectionId, out var playerConnection))
                    await playerConnection.SendAsync(rejectedEvent, ct).ConfigureAwait(false);
            }
        }
    }
}
