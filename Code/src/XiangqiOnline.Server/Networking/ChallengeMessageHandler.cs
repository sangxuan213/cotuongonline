using System.Text.Json;
using XiangqiOnline.Server.Lobby;
using XiangqiOnline.Shared.Models;
using XiangqiOnline.Shared.Protocol;

namespace XiangqiOnline.Server.Networking
{
    public static class ChallengeMessageHandler
    {
        public static readonly TimeSpan ChallengeLifetime = TimeSpan.FromSeconds(60);
        public const string DefaultTimeProfile = "STANDARD_PRO";

        public static async Task HandleAsync(
            RequestEnvelope<JsonElement> request,
            ClientConnectionHandler connection,
            PlayerSessionDirectory directory,
            ChallengeManager challenges,
            IConnectionRegistry connections,
            CancellationToken ct)
        {
            if (!directory.TryGetByConnectionId(connection.ConnectionId, out var sender) ||
                !directory.ValidateSessionToken(sender, request.SessionToken))
            {
                await connection.SendErrorAsync(ErrorCodes.INVALID_SESSION, "Sender is not logged in.", request.RequestId, ct).ConfigureAwait(false);
                return;
            }

            var value = request.Payload;
            if (!value.TryGetProperty("targetPlayerId", out var targetNode) ||
                string.IsNullOrWhiteSpace(targetNode.GetString()))
            {
                await connection.SendErrorAsync(ErrorCodes.INVALID_SESSION, "CHALLENGE_SEND requires 'targetPlayerId'.", request.RequestId, ct).ConfigureAwait(false);
                return;
            }

            var targetPlayerId = targetNode.GetString()!;
            var timeProfile = value.TryGetProperty("timeProfile", out var profileNode) &&
                              !string.IsNullOrWhiteSpace(profileNode.GetString())
                ? profileNode.GetString()!
                : DefaultTimeProfile;

            try { _ = TimeProfileSpec.Parse(timeProfile); }
            catch (ArgumentException)
            {
                await connection.SendErrorAsync(ErrorCodes.INVALID_MESSAGE_SCHEMA, "Unsupported time profile.", request.RequestId, ct).ConfigureAwait(false);
                return;
            }

            var result = challenges.SendChallenge(sender.PlayerId, targetPlayerId, timeProfile, DateTimeOffset.UtcNow, ChallengeLifetime);
            if (!result.IsSuccess)
            {
                await connection.SendErrorAsync(result.ErrorCode ?? ErrorCodes.PLAYER_NOT_AVAILABLE, result.Message, request.RequestId, ct).ConfigureAwait(false);
                return;
            }

            var challenge = result.Challenge!;
            if (directory.TryGetByPlayerId(targetPlayerId, out var target) &&
                connections.TryGetConnection(target.ConnectionId, out var targetConnection))
            {
                try
                {
                    await targetConnection.SendAsync(new ServerEventEnvelope<object>
                    {
                        Type = "CHALLENGE_RECEIVED",
                        EventId = Guid.NewGuid().ToString("N"),
                        CausationRequestId = request.RequestId,
                        ServerTimeUtc = DateTimeOffset.UtcNow,
                        Payload = new
                        {
                            challenge = new
                            {
                                challengeId = challenge.ChallengeId,
                                fromPlayerId = challenge.ChallengerPlayerId,
                                fromDisplayName = sender.DisplayName
                            }
                        }
                    }, ct).ConfigureAwait(false);

                    await connection.SendAsync(new ServerEventEnvelope<object>
                    {
                        Type = "CHALLENGE_SENT",
                        EventId = Guid.NewGuid().ToString("N"),
                        CausationRequestId = request.RequestId,
                        ServerTimeUtc = DateTimeOffset.UtcNow,
                        Payload = new
                        {
                            challengeId = challenge.ChallengeId,
                            targetPlayerId = challenge.TargetPlayerId,
                            targetDisplayName = target.DisplayName,
                            expiresAtUtc = challenge.ExpiresAtUtc
                        }
                    }, ct).ConfigureAwait(false);
                }
                catch
                {
                    challenges.CancelChallenge(challenge.ChallengeId, sender.PlayerId);
                    await connection.SendErrorAsync(ErrorCodes.PLAYER_NOT_AVAILABLE,
                        "Target disconnected before the challenge could be delivered.", request.RequestId, ct).ConfigureAwait(false);
                }
            }
            else
            {
                challenges.CancelChallenge(challenge.ChallengeId, sender.PlayerId);
                await connection.SendErrorAsync(ErrorCodes.PLAYER_NOT_AVAILABLE,
                    "Target is no longer connected.", request.RequestId, ct).ConfigureAwait(false);
            }
        }
    }
}
