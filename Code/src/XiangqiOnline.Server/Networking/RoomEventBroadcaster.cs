using XiangqiOnline.Server.Lobby;
using XiangqiOnline.Shared.Protocol;

namespace XiangqiOnline.Server.Networking;

public static class RoomEventBroadcaster
{
    public static async Task BroadcastAsync(
        GameRoom room,
        PlayerSessionDirectory players,
        IConnectionRegistry connections,
        ServerEventEnvelope<object> envelope,
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(envelope.Type, "GAME_ENDED", StringComparison.Ordinal) &&
            !room.TryMarkGameEndedBroadcasted()) return;

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var playerId in new[] { room.RedPlayerId, room.BlackPlayerId })
        {
            if (players.TryGetByPlayerId(playerId, out var player)) ids.Add(player.ConnectionId);
        }
        foreach (var spectator in room.SpectatorConnectionIds) ids.Add(spectator);

        foreach (var id in ids)
        {
            if (!connections.TryGetConnection(id, out var target)) continue;
            try { await target.SendAsync(envelope, cancellationToken).ConfigureAwait(false); }
            catch (Exception exception)
            {
                ServerConsoleLog.Warning("KHÁN GIẢ", $"Ngắt khán giả {id} khỏi phòng {room.RoomId}: {exception.Message}");
                room.RemoveSpectator(id);
            }
        }
    }
}
