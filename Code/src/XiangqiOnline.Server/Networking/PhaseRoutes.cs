using System.Text.Json;
using XiangqiOnline.Persistence.Services;
using XiangqiOnline.Server.Lobby;
using XiangqiOnline.Shared.Models;
using XiangqiOnline.Shared.Protocol;

namespace XiangqiOnline.Server.Networking;

public static class PhaseRoutes
{
    public static void Register(
        MessageRouter router,
        PlayerSessionDirectory players,
        ChallengeManager challenges,
        IConnectionRegistry connections,
        GamePersistenceService persistence,
        BotMoveService? bots = null)
    {
        router.Register("PING", GameControlMessageHandler.PingAsync);
        router.Register("RECONNECT_REQUEST", (request, connection, ct) =>
            GameControlMessageHandler.ReconnectAsync(request, connection, players, challenges, ct));
        router.Register("ACTIVE_MATCHES_REQUEST", (request, connection, ct) =>
            GameControlMessageHandler.ActiveMatchesAsync(request, connection, challenges, players, ct));
        router.Register("SPECTATOR_JOIN", (request, connection, ct) =>
            GameControlMessageHandler.SpectatorJoinAsync(request, connection, challenges, players, ct));
        router.Register("SPECTATOR_LEAVE", (request, connection, ct) =>
            GameControlMessageHandler.SpectatorLeaveAsync(request, connection, challenges, players, ct));
        router.Register("RESYNC_REQUEST", (request, connection, ct) =>
            GameControlMessageHandler.ResyncAsync(request, connection, challenges, players, ct));
        router.Register("RESIGN_REQUEST", (request, connection, ct) =>
            GameControlMessageHandler.ResignAsync(request, connection, challenges, players, connections, persistence, ct));
        router.Register("DRAW_OFFER", (request, connection, ct) =>
            GameControlMessageHandler.DrawOfferAsync(request, connection, challenges, players, connections, ct));
        router.Register("DRAW_RESPONSE", (request, connection, ct) =>
            GameControlMessageHandler.DrawResponseAsync(request, connection, challenges, players, connections, persistence, ct));
        router.Register("REMATCH_REQUEST", (request, connection, ct) =>
            GameControlMessageHandler.RematchRequestAsync(request, connection, challenges, players, connections, ct));
        router.Register("REMATCH_RESPONSE", (request, connection, ct) =>
            GameControlMessageHandler.RematchResponseAsync(request, connection, challenges, players, connections, ct));
        router.Register("REMATCH_CANCEL", (request, connection, ct) =>
            GameControlMessageHandler.RematchCancelAsync(request, connection, challenges, players, connections, ct));
        router.Register("CHALLENGE_CANCEL", (request, connection, ct) =>
            CancelChallengeAsync(request, connection, players, challenges, connections, ct));
        router.Register("HISTORY_LIST_REQUEST", (request, connection, ct) =>
            HistoryMessageHandler.ListAsync(request, connection, players, persistence, ct));
        router.Register("HISTORY_DETAIL_REQUEST", (request, connection, ct) =>
            HistoryMessageHandler.DetailAsync(request, connection, players, persistence, ct));
        if (bots is not null)
            router.Register("BOT_GAME_REQUEST", (request, connection, ct) =>
                BotGameMessageHandler.HandleAsync(request, connection, players, challenges, bots, ct));
    }

    private static async Task CancelChallengeAsync(
        RequestEnvelope<JsonElement> request,
        ClientConnectionHandler connection,
        PlayerSessionDirectory players,
        ChallengeManager challenges,
        IConnectionRegistry connections,
        CancellationToken ct)
    {
        if (!players.TryGetByConnectionId(connection.ConnectionId, out var player) ||
            !players.ValidateSessionToken(player, request.SessionToken))
        {
            await connection.SendErrorAsync(ErrorCodes.UNAUTHENTICATED, "Login is required.", request.RequestId, ct).ConfigureAwait(false);
            return;
        }
        if (!request.Payload.TryGetProperty("challengeId", out var node) || string.IsNullOrWhiteSpace(node.GetString()))
        {
            await connection.SendErrorAsync(ErrorCodes.INVALID_MESSAGE_SCHEMA, "challengeId is required.", request.RequestId, ct).ConfigureAwait(false);
            return;
        }
        var result = challenges.CancelChallenge(node.GetString()!, player.PlayerId);
        if (!result.IsSuccess)
        {
            await connection.SendErrorAsync(result.ErrorCode!, result.Message, request.RequestId, ct).ConfigureAwait(false);
            return;
        }
        var challenge = result.Challenge!;
        var cancelled = new ServerEventEnvelope<object>
        {
            Type = "CHALLENGE_CANCELLED",
            EventId = Guid.NewGuid().ToString("N"),
            CausationRequestId = request.RequestId,
            ServerTimeUtc = DateTimeOffset.UtcNow,
            Payload = new { challengeId = challenge.ChallengeId, status = "CANCELLED" }
        };
        foreach (var playerId in new[] { challenge.ChallengerPlayerId, challenge.TargetPlayerId })
        {
            if (!players.TryGetByPlayerId(playerId, out var recipient) ||
                !connections.TryGetConnection(recipient.ConnectionId, out var target)) continue;
            try { await target.SendAsync(cancelled, ct).ConfigureAwait(false); }
            catch (Exception exception)
            {
                ServerConsoleLog.Warning("THÁCH ĐẤU", $"Không thể báo hủy tới {playerId}: {exception.Message}");
            }
        }
    }
}
