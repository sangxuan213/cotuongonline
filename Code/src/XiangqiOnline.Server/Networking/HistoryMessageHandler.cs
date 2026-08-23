using System.Text.Json;
using XiangqiOnline.Persistence.Services;
using XiangqiOnline.Server.Lobby;
using XiangqiOnline.Shared.Models;
using XiangqiOnline.Shared.Protocol;

namespace XiangqiOnline.Server.Networking;

public static class HistoryMessageHandler
{
    public static async Task ListAsync(
        RequestEnvelope<JsonElement> request,
        ClientConnectionHandler connection,
        PlayerSessionDirectory players,
        GamePersistenceService persistence,
        CancellationToken ct)
    {
        if (!players.TryGetByConnectionId(connection.ConnectionId, out var player) ||
            !string.Equals(player.PlayerId, request.SessionToken, StringComparison.Ordinal))
        {
            await connection.SendErrorAsync(ErrorCodes.UNAUTHENTICATED, "Login is required.", request.RequestId, ct).ConfigureAwait(false);
            return;
        }
        var matches = persistence.ListMatchesByPlayer(player.PlayerId)
            .Where(match => !match.Status.Equals("PLAYING", StringComparison.OrdinalIgnoreCase))
            .Select(match => new
        {
            match.MatchId, match.RoomId, match.RedPlayerId, match.BlackPlayerId, match.Status,
            match.StartedAtUtc, match.EndedAtUtc, match.ResultType, match.EndReason, match.WinnerSide,
            match.FinalRevision, match.TotalMoves, match.TimeProfile,
            ViewerSide = match.RedPlayerId == player.PlayerId ? "RED" : "BLACK",
            RedDisplayName = persistence.ResolvePlayerDisplayName(match.RedPlayerId),
            BlackDisplayName = persistence.ResolvePlayerDisplayName(match.BlackPlayerId)
        }).ToArray();
        await connection.SendAsync(new ServerEventEnvelope<object>
        {
            Type = "HISTORY_LIST_RESULT", EventId = Guid.NewGuid().ToString("N"), CausationRequestId = request.RequestId,
            ServerTimeUtc = DateTimeOffset.UtcNow, Payload = new { matches }
        }, ct).ConfigureAwait(false);
    }

    public static async Task DetailAsync(
        RequestEnvelope<JsonElement> request,
        ClientConnectionHandler connection,
        PlayerSessionDirectory players,
        GamePersistenceService persistence,
        CancellationToken ct)
    {
        if (!players.TryGetByConnectionId(connection.ConnectionId, out var player) ||
            !string.Equals(player.PlayerId, request.SessionToken, StringComparison.Ordinal))
        {
            await connection.SendErrorAsync(ErrorCodes.UNAUTHENTICATED, "Login is required.", request.RequestId, ct).ConfigureAwait(false);
            return;
        }
        var matchId = request.Payload.TryGetProperty("matchId", out var node) ? node.GetString() : null;
        var history = new HistoryService(persistence);
        var detail = matchId is null ? null : history.GetDetail(matchId);
        if (detail is null || (detail.Match.RedPlayerId != player.PlayerId && detail.Match.BlackPlayerId != player.PlayerId))
        {
            await connection.SendErrorAsync(ErrorCodes.ROOM_NOT_FOUND, "Match history was not found.", request.RequestId, ct).ConfigureAwait(false);
            return;
        }
        await connection.SendAsync(new ServerEventEnvelope<object>
        {
            Type = "HISTORY_DETAIL_RESULT", EventId = Guid.NewGuid().ToString("N"), CausationRequestId = request.RequestId,
            RoomId = detail.Match.RoomId, Revision = detail.Match.FinalRevision, ServerTimeUtc = DateTimeOffset.UtcNow,
            Payload = new { match = detail.Match, moves = detail.Moves, positions = detail.Positions }
        }, ct).ConfigureAwait(false);
    }
}
