using System.Text.Json;
using XiangqiOnline.Server.Lobby;
using XiangqiOnline.Shared.Models;
using XiangqiOnline.Shared.Protocol;

namespace XiangqiOnline.Server.Networking;

public static class WaitingRoomMessageHandler
{
    public static async Task CreateAsync(
        RequestEnvelope<JsonElement> request,
        ClientConnectionHandler connection,
        PlayerSessionDirectory players,
        ChallengeManager challenges,
        IConnectionRegistry connections,
        CancellationToken ct)
    {
        if (!TryAuthenticate(request, connection, players, out var player))
        {
            await connection.SendErrorAsync(ErrorCodes.UNAUTHENTICATED, "Login is required.", request.RequestId, ct).ConfigureAwait(false);
            return;
        }
        var password = ReadPassword(request.Payload);
        if (password is { Length: > 24 })
        {
            await connection.SendErrorAsync(ErrorCodes.INVALID_MESSAGE_SCHEMA, "Mật khẩu phòng tối đa 24 ký tự.", request.RequestId, ct).ConfigureAwait(false);
            return;
        }
        if (!challenges.TryCreateWaitingRoom(player.PlayerId, password, DateTimeOffset.UtcNow, out var waitingRoom, out var error))
        {
            await connection.SendErrorAsync(ErrorCodes.PLAYER_NOT_AVAILABLE, error, request.RequestId, ct).ConfigureAwait(false);
            return;
        }

        ServerConsoleLog.Success("TẠO PHÒNG", $"{player.DisplayName} tạo phòng {waitingRoom.RoomId} • 10+0 • {(waitingRoom.IsLocked ? "có khóa" : "công khai")}");
        await connection.SendAsync(new ServerEventEnvelope<object>
        {
            Type = "WAITING_ROOM_CREATED",
            EventId = Guid.NewGuid().ToString("N"),
            CausationRequestId = request.RequestId,
            ServerTimeUtc = DateTimeOffset.UtcNow,
            Payload = ToWire(waitingRoom)
        }, ct).ConfigureAwait(false);
        await BroadcastListAsync(players, challenges, connections, request.RequestId, ct).ConfigureAwait(false);
    }

    public static async Task ListAsync(
        RequestEnvelope<JsonElement> request,
        ClientConnectionHandler connection,
        PlayerSessionDirectory players,
        ChallengeManager challenges,
        CancellationToken ct)
    {
        if (!TryAuthenticate(request, connection, players, out _))
        {
            await connection.SendErrorAsync(ErrorCodes.UNAUTHENTICATED, "Login is required.", request.RequestId, ct).ConfigureAwait(false);
            return;
        }
        await connection.SendAsync(CreateListEvent(challenges, request.RequestId), ct).ConfigureAwait(false);
    }

    public static async Task JoinAsync(
        RequestEnvelope<JsonElement> request,
        ClientConnectionHandler connection,
        PlayerSessionDirectory players,
        ChallengeManager challenges,
        IConnectionRegistry connections,
        CancellationToken ct)
    {
        if (!TryAuthenticate(request, connection, players, out var player))
        {
            await connection.SendErrorAsync(ErrorCodes.UNAUTHENTICATED, "Login is required.", request.RequestId, ct).ConfigureAwait(false);
            return;
        }
        var roomId = request.Payload.TryGetProperty("roomId", out var node) ? node.GetString() : request.RoomId;
        if (string.IsNullOrWhiteSpace(roomId))
        {
            await connection.SendErrorAsync(ErrorCodes.ROOM_NOT_FOUND, "roomId is required.", request.RequestId, ct).ConfigureAwait(false);
            return;
        }
        var password = ReadPassword(request.Payload);
        if (!challenges.TryJoinWaitingRoom(roomId, player.PlayerId, password, DateTimeOffset.UtcNow, out var room, out var error))
        {
            await connection.SendErrorAsync(ErrorCodes.ROOM_NOT_FOUND, error, request.RequestId, ct).ConfigureAwait(false);
            return;
        }

        ServerConsoleLog.Success("VÀO PHÒNG", $"{player.DisplayName} vào phòng {room.RoomId} • ván đấu bắt đầu");
        foreach (var playerId in new[] { room.RedPlayerId, room.BlackPlayerId })
        {
            if (!players.TryGetByPlayerId(playerId, out var participant) ||
                !connections.TryGetConnection(participant.ConnectionId, out var target)) continue;
            try
            {
                await target.SendAsync(RoomMessages.RoomCreated(room, request.RequestId), ct).ConfigureAwait(false);
                var role = playerId == room.RedPlayerId ? "PLAYER_RED" : "PLAYER_BLACK";
                await target.SendAsync(RoomMessages.GameStateSnapshot(room, request.RequestId, role), ct).ConfigureAwait(false);
            }
            catch { /* A disconnected peer cannot block the other participant. */ }
        }
        await BroadcastListAsync(players, challenges, connections, request.RequestId, ct).ConfigureAwait(false);
    }

    public static async Task CancelAsync(
        RequestEnvelope<JsonElement> request,
        ClientConnectionHandler connection,
        PlayerSessionDirectory players,
        ChallengeManager challenges,
        IConnectionRegistry connections,
        CancellationToken ct)
    {
        if (!TryAuthenticate(request, connection, players, out var player))
        {
            await connection.SendErrorAsync(ErrorCodes.UNAUTHENTICATED, "Login is required.", request.RequestId, ct).ConfigureAwait(false);
            return;
        }
        if (!challenges.RemoveWaitingRoomForPlayer(player.PlayerId))
        {
            await connection.SendErrorAsync(ErrorCodes.ROOM_NOT_FOUND, "You do not own an open waiting room.", request.RequestId, ct).ConfigureAwait(false);
            return;
        }

        ServerConsoleLog.Info("HỦY PHÒNG", $"{player.DisplayName} đã hủy phòng chờ");
        await connection.SendAsync(new ServerEventEnvelope<object>
        {
            Type = "WAITING_ROOM_CANCELLED",
            EventId = Guid.NewGuid().ToString("N"),
            CausationRequestId = request.RequestId,
            ServerTimeUtc = DateTimeOffset.UtcNow,
            Payload = new { roomId = request.RoomId }
        }, ct).ConfigureAwait(false);
        await BroadcastListAsync(players, challenges, connections, request.RequestId, ct).ConfigureAwait(false);
    }

    private static bool TryAuthenticate(
        RequestEnvelope<JsonElement> request,
        ClientConnectionHandler connection,
        PlayerSessionDirectory players,
        out PlayerSession player) =>
        players.TryGetByConnectionId(connection.ConnectionId, out player!) &&
        players.ValidateSessionToken(player, request.SessionToken);

    private static object ToWire(WaitingRoom room) => new
    {
        roomId = room.RoomId,
        ownerPlayerId = room.OwnerPlayerId,
        ownerDisplayName = room.OwnerDisplayName,
        timeProfile = room.TimeProfile,
        createdAtUtc = room.CreatedAtUtc,
        isLocked = room.IsLocked
    };

    private static string? ReadPassword(JsonElement payload)
    {
        if (!payload.TryGetProperty("password", out var node) || node.ValueKind != JsonValueKind.String) return null;
        var value = node.GetString()?.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static ServerEventEnvelope<object> CreateListEvent(ChallengeManager challenges, string? requestId) => new()
    {
        Type = "WAITING_ROOMS_UPDATED",
        EventId = Guid.NewGuid().ToString("N"),
        CausationRequestId = requestId,
        ServerTimeUtc = DateTimeOffset.UtcNow,
        Payload = new { rooms = challenges.GetWaitingRoomsSnapshot().Select(ToWire).ToArray() }
    };

    private static async Task BroadcastListAsync(
        PlayerSessionDirectory players,
        ChallengeManager challenges,
        IConnectionRegistry connections,
        string? requestId,
        CancellationToken ct)
    {
        var message = CreateListEvent(challenges, requestId);
        foreach (var entry in players.GetSnapshot())
        {
            if (!players.TryGetByPlayerId(entry.PlayerId, out var player) ||
                !connections.TryGetConnection(player.ConnectionId, out var target)) continue;
            try { await target.SendAsync(message, ct).ConfigureAwait(false); }
            catch { /* Best-effort lobby broadcast. */ }
        }
    }
}
