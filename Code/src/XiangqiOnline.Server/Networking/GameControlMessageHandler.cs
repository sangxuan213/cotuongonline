using System.Text.Json;
using XiangqiOnline.Persistence.Services;
using XiangqiOnline.Server.Lobby;
using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;
using XiangqiOnline.Shared.Protocol;

namespace XiangqiOnline.Server.Networking;

public static class GameControlMessageHandler
{
    public static Task PingAsync(RequestEnvelope<JsonElement> request, ClientConnectionHandler connection, CancellationToken ct) =>
        connection.SendAsync(new ServerEventEnvelope<object>
        {
            Type = "PONG",
            EventId = Guid.NewGuid().ToString("N"),
            CausationRequestId = request.RequestId,
            ServerTimeUtc = DateTimeOffset.UtcNow,
            Payload = request.Payload.ValueKind == JsonValueKind.Undefined ? new { } : request.Payload
        }, ct);

    public static async Task ReconnectAsync(
        RequestEnvelope<JsonElement> request,
        ClientConnectionHandler connection,
        PlayerSessionDirectory players,
        ChallengeManager challenges,
        CancellationToken ct)
    {
        var token = ReadString(request.Payload, "resumeToken") ?? request.SessionToken;
        var result = players.ResumeByToken(token ?? string.Empty, connection.ConnectionId, DateTimeOffset.UtcNow);
        if (!result.IsSuccess)
        {
            await connection.SendErrorAsync(result.ErrorCode!, result.Message, request.RequestId, ct).ConfigureAwait(false);
            return;
        }

        var session = result.Session!;
        await connection.SendAsync(new ServerEventEnvelope<object>
        {
            Type = "RECONNECT_ACCEPTED",
            EventId = Guid.NewGuid().ToString("N"),
            CausationRequestId = request.RequestId,
            RoomId = session.RoomId,
            ServerTimeUtc = DateTimeOffset.UtcNow,
            Payload = new { playerId = session.PlayerId, roomId = session.RoomId }
        }, ct).ConfigureAwait(false);

        if (session.RoomId is not null && challenges.TryGetRoom(session.RoomId, out var room))
            await connection.SendAsync(RoomMessages.GameStateSnapshot(room, request.RequestId,
                room.GetSideForPlayer(session.PlayerId) == SideColor.Red ? "PLAYER_RED" : "PLAYER_BLACK"), ct).ConfigureAwait(false);
    }

    public static async Task ActiveMatchesAsync(
        RequestEnvelope<JsonElement> request,
        ClientConnectionHandler connection,
        ChallengeManager challenges,
        PlayerSessionDirectory players,
        CancellationToken ct)
    {
        if (!players.TryGetByConnectionId(connection.ConnectionId, out var activePlayer) ||
            !players.ValidateSessionToken(activePlayer, request.SessionToken))
        {
            await connection.SendErrorAsync(ErrorCodes.UNAUTHENTICATED, "Login is required.", request.RequestId, ct).ConfigureAwait(false);
            return;
        }

        var matches = challenges.GetRoomsSnapshot(activeOnly: true).Select(room =>
        {
            players.TryGetByPlayerId(room.RedPlayerId, out var red);
            players.TryGetByPlayerId(room.BlackPlayerId, out var black);
            return new
            {
                roomId = room.RoomId,
                redPlayerId = room.RedPlayerId,
                blackPlayerId = room.BlackPlayerId,
                redDisplayName = red?.DisplayName ?? "Bên Đỏ",
                blackDisplayName = black?.DisplayName ?? (room.BlackPlayerId.StartsWith("BOT_", StringComparison.Ordinal) ? "Máy" : "Bên Đen"),
                currentTurn = room.CurrentTurn.ToString().ToUpperInvariant(),
                timeProfile = room.TimeProfile,
                spectatorCount = room.SpectatorConnectionIds.Count,
                revision = room.Revision
            };
        }).ToArray();
        await connection.SendAsync(new ServerEventEnvelope<object>
        {
            Type = "ACTIVE_MATCHES_UPDATED",
            EventId = Guid.NewGuid().ToString("N"),
            CausationRequestId = request.RequestId,
            ServerTimeUtc = DateTimeOffset.UtcNow,
            Payload = new { matches }
        }, ct).ConfigureAwait(false);
    }

    public static async Task SpectatorJoinAsync(
        RequestEnvelope<JsonElement> request,
        ClientConnectionHandler connection,
        ChallengeManager challenges,
        PlayerSessionDirectory players,
        CancellationToken ct)
    {
        if (!players.TryGetByConnectionId(connection.ConnectionId, out var session) ||
            !players.ValidateSessionToken(session, request.SessionToken))
        {
            await connection.SendErrorAsync(ErrorCodes.UNAUTHENTICATED, "Login is required.", request.RequestId, ct).ConfigureAwait(false);
            return;
        }
        var roomId = request.RoomId ?? ReadString(request.Payload, "roomId");
        if (roomId is null || !challenges.TryGetRoom(roomId, out var room) || room.IsTerminal)
        {
            await connection.SendErrorAsync(ErrorCodes.ROOM_NOT_FOUND, "Active room was not found.", request.RequestId, ct).ConfigureAwait(false);
            return;
        }
        if (room.HasPlayer(session.PlayerId))
        {
            await connection.SendErrorAsync(ErrorCodes.SPECTATOR_ACTION_NOT_ALLOWED, "A seated player cannot spectate their own room.", request.RequestId, ct).ConfigureAwait(false);
            return;
        }
        challenges.RemoveConnectionFromSpectators(connection.ConnectionId);
        room.AddSpectator(connection.ConnectionId);
        await connection.SendAsync(RoomMessages.GameStateSnapshot(room, request.RequestId, "SPECTATOR"), ct).ConfigureAwait(false);
    }

    public static async Task SpectatorLeaveAsync(
        RequestEnvelope<JsonElement> request,
        ClientConnectionHandler connection,
        ChallengeManager challenges,
        PlayerSessionDirectory players,
        CancellationToken ct)
    {
        if (!players.TryGetByConnectionId(connection.ConnectionId, out var session) ||
            !players.ValidateSessionToken(session, request.SessionToken))
        {
            await connection.SendErrorAsync(ErrorCodes.UNAUTHENTICATED, "Login is required.", request.RequestId, ct).ConfigureAwait(false);
            return;
        }
        var roomId = request.RoomId ?? ReadString(request.Payload, "roomId");
        if (roomId is not null && challenges.TryGetRoom(roomId, out var room))
            room.RemoveSpectator(connection.ConnectionId);
        await connection.SendAsync(new ServerEventEnvelope<object>
        {
            Type = "SPECTATOR_LEFT",
            EventId = Guid.NewGuid().ToString("N"),
            CausationRequestId = request.RequestId,
            RoomId = roomId,
            ServerTimeUtc = DateTimeOffset.UtcNow,
            Payload = new { roomId }
        }, ct).ConfigureAwait(false);
    }

    public static async Task ResyncAsync(
        RequestEnvelope<JsonElement> request,
        ClientConnectionHandler connection,
        ChallengeManager challenges,
        PlayerSessionDirectory players,
        CancellationToken ct)
    {
        var roomId = request.RoomId ?? ReadString(request.Payload, "roomId");
        if (roomId is null || !challenges.TryGetRoom(roomId, out var room))
        {
            await connection.SendErrorAsync(ErrorCodes.ROOM_NOT_FOUND, "Room was not found.", request.RequestId, ct).ConfigureAwait(false);
            return;
        }
        var role = "SPECTATOR";
        if (players.TryGetByConnectionId(connection.ConnectionId, out var session) &&
            players.ValidateSessionToken(session, request.SessionToken) && room.HasPlayer(session.PlayerId))
            role = room.GetSideForPlayer(session.PlayerId) == SideColor.Red ? "PLAYER_RED" : "PLAYER_BLACK";
        else if (!room.SpectatorConnectionIds.Contains(connection.ConnectionId))
        {
            await connection.SendErrorAsync(ErrorCodes.NOT_ROOM_MEMBER, "Connection is not a room member.", request.RequestId, ct).ConfigureAwait(false);
            return;
        }
        await connection.SendAsync(RoomMessages.GameStateSnapshot(room, request.RequestId, role), ct).ConfigureAwait(false);
    }

    public static async Task ResignAsync(
        RequestEnvelope<JsonElement> request,
        ClientConnectionHandler connection,
        ChallengeManager challenges,
        PlayerSessionDirectory players,
        IConnectionRegistry connections,
        GamePersistenceService persistence,
        CancellationToken ct)
    {
        if (!TryResolvePlayerRoom(request, connection, challenges, players, out var session, out var room, out var error))
        {
            await connection.SendErrorAsync(error, "Player room could not be resolved.", request.RequestId, ct).ConfigureAwait(false);
            return;
        }
        await room.ExecuteSerializedAsync(async () =>
        {
            var loser = room.GetSideForPlayer(session.PlayerId);
            var winner = loser == SideColor.Red ? SideColor.Black : SideColor.Red;
            var result = new GameResult(winner == SideColor.Red ? "RED_WIN" : "BLACK_WIN", "RESIGNATION", winner,
                DateTimeOffset.UtcNow, room.Revision, $"{loser} resigned.");
            if (!room.TryFinish(result))
            {
                await connection.SendErrorAsync(ErrorCodes.GAME_ALREADY_FINISHED, "Game already finished.", request.RequestId, ct).ConfigureAwait(false);
                return false;
            }
            TryPersistCompletion(persistence, room, result);
            players.LeaveRoom(room.RedPlayerId);
            players.LeaveRoom(room.BlackPlayerId);
            await RoomEventBroadcaster.BroadcastAsync(room, players, connections, RoomMessages.GameEnded(room, request.RequestId), ct).ConfigureAwait(false);
            return true;
        }, ct).ConfigureAwait(false);
    }

    public static async Task DrawOfferAsync(
        RequestEnvelope<JsonElement> request,
        ClientConnectionHandler connection,
        ChallengeManager challenges,
        PlayerSessionDirectory players,
        IConnectionRegistry connections,
        CancellationToken ct)
    {
        if (!TryResolvePlayerRoom(request, connection, challenges, players, out var session, out var room, out var error))
        {
            await connection.SendErrorAsync(error, "Player room could not be resolved.", request.RequestId, ct).ConfigureAwait(false);
            return;
        }
        if (room.BlackPlayerId.StartsWith("BOT_", StringComparison.Ordinal))
        {
            await connection.SendErrorAsync("ACTION_NOT_SUPPORTED",
                "Đấu với máy không hỗ trợ đề nghị hòa; bạn có thể tiếp tục hoặc xin thua.",
                request.RequestId, ct).ConfigureAwait(false);
            return;
        }
        var nowUtc = DateTimeOffset.UtcNow;
        var drawLifetime = TimeSpan.FromSeconds(30);
        if (!room.TryOfferDraw(session.PlayerId, nowUtc, drawLifetime))
        {
            await connection.SendErrorAsync(ErrorCodes.DRAW_OFFER_ALREADY_PENDING, "A draw offer is already pending.", request.RequestId, ct).ConfigureAwait(false);
            return;
        }
        await RoomEventBroadcaster.BroadcastAsync(room, players, connections, new ServerEventEnvelope<object>
        {
            Type = "DRAW_OFFERED",
            EventId = Guid.NewGuid().ToString("N"),
            CausationRequestId = request.RequestId,
            RoomId = room.RoomId,
            Revision = room.Revision,
            ServerTimeUtc = DateTimeOffset.UtcNow,
            Payload = new { offeredBy = session.PlayerId, expiresAt = nowUtc.Add(drawLifetime) }
        }, ct).ConfigureAwait(false);
    }

    public static async Task DrawResponseAsync(
        RequestEnvelope<JsonElement> request,
        ClientConnectionHandler connection,
        ChallengeManager challenges,
        PlayerSessionDirectory players,
        IConnectionRegistry connections,
        GamePersistenceService persistence,
        CancellationToken ct)
    {
        if (!TryResolvePlayerRoom(request, connection, challenges, players, out var session, out var room, out var error))
        {
            await connection.SendErrorAsync(error, "Player room could not be resolved.", request.RequestId, ct).ConfigureAwait(false);
            return;
        }
        if (!request.Payload.TryGetProperty("accept", out var node) || node.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            await connection.SendErrorAsync(ErrorCodes.INVALID_MESSAGE_SCHEMA, "DRAW_RESPONSE requires a boolean accept field.", request.RequestId, ct).ConfigureAwait(false);
            return;
        }
        var accept = node.GetBoolean();
        await room.ExecuteSerializedAsync(async () =>
        {
            if (!room.TryRespondToDraw(session.PlayerId, accept, DateTimeOffset.UtcNow))
            {
                await connection.SendErrorAsync(ErrorCodes.INVALID_MESSAGE_SCHEMA, "No unexpired opponent draw offer is pending.", request.RequestId, ct).ConfigureAwait(false);
                return false;
            }
            if (!accept)
            {
                await RoomEventBroadcaster.BroadcastAsync(room, players, connections, new ServerEventEnvelope<object>
                {
                    Type = "DRAW_DECLINED",
                    EventId = Guid.NewGuid().ToString("N"),
                    CausationRequestId = request.RequestId,
                    RoomId = room.RoomId,
                    Revision = room.Revision,
                    ServerTimeUtc = DateTimeOffset.UtcNow,
                    Payload = new { declinedBy = session.PlayerId }
                }, ct).ConfigureAwait(false);
                return true;
            }
            var result = new GameResult("DRAW", "DRAW_AGREEMENT", null, DateTimeOffset.UtcNow, room.Revision, "Both players agreed to a draw.");
            if (!room.TryFinish(result)) return false;
            TryPersistCompletion(persistence, room, result);
            players.LeaveRoom(room.RedPlayerId);
            players.LeaveRoom(room.BlackPlayerId);
            await RoomEventBroadcaster.BroadcastAsync(room, players, connections, RoomMessages.GameEnded(room, request.RequestId), ct).ConfigureAwait(false);
            return true;
        }, ct).ConfigureAwait(false);
    }

    public static async Task RematchRequestAsync(
        RequestEnvelope<JsonElement> request,
        ClientConnectionHandler connection,
        ChallengeManager challenges,
        PlayerSessionDirectory players,
        IConnectionRegistry connections,
        CancellationToken ct)
    {
        if (!TryResolveTerminalPlayerRoom(request, connection, challenges, players, out var session, out var room, out var error))
        {
            await connection.SendErrorAsync(error, "Không thể xác định ván đấu đã kết thúc.", request.RequestId, ct).ConfigureAwait(false);
            return;
        }
        if (!challenges.TryRequestRematch(room.RoomId, session.PlayerId, DateTimeOffset.UtcNow,
                TimeSpan.FromSeconds(60), out var offer, out var message))
        {
            await connection.SendErrorAsync("REMATCH_NOT_AVAILABLE", message, request.RequestId, ct).ConfigureAwait(false);
            return;
        }

        ServerConsoleLog.Info("ĐẤU LẠI", $"{session.DisplayName} yêu cầu đấu lại phòng {room.RoomId}");
        await SendToPlayersAsync(room, players, connections, new ServerEventEnvelope<object>
        {
            Type = "REMATCH_OFFERED",
            EventId = Guid.NewGuid().ToString("N"),
            CausationRequestId = request.RequestId,
            RoomId = room.RoomId,
            Revision = room.Revision,
            ServerTimeUtc = DateTimeOffset.UtcNow,
            Payload = new
            {
                originalRoomId = room.RoomId,
                requestedBy = offer.RequesterPlayerId,
                targetPlayerId = offer.TargetPlayerId,
                expiresAtUtc = offer.ExpiresAtUtc
            }
        }, ct).ConfigureAwait(false);
    }

    public static async Task RematchResponseAsync(
        RequestEnvelope<JsonElement> request,
        ClientConnectionHandler connection,
        ChallengeManager challenges,
        PlayerSessionDirectory players,
        IConnectionRegistry connections,
        CancellationToken ct)
    {
        if (!players.TryGetByConnectionId(connection.ConnectionId, out var session) ||
            !players.ValidateSessionToken(session, request.SessionToken))
        {
            await connection.SendErrorAsync(ErrorCodes.UNAUTHENTICATED, "Bạn cần đăng nhập.", request.RequestId, ct).ConfigureAwait(false);
            return;
        }
        var originalRoomId = request.RoomId ?? ReadString(request.Payload, "originalRoomId");
        if (string.IsNullOrWhiteSpace(originalRoomId) ||
            !request.Payload.TryGetProperty("accept", out var acceptNode) ||
            acceptNode.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            await connection.SendErrorAsync(ErrorCodes.INVALID_MESSAGE_SCHEMA,
                "REMATCH_RESPONSE cần originalRoomId và accept.", request.RequestId, ct).ConfigureAwait(false);
            return;
        }

        var accept = acceptNode.GetBoolean();
        if (!challenges.TryRespondToRematch(originalRoomId, session.PlayerId, accept, DateTimeOffset.UtcNow,
                out var offer, out var newRoom, out var message))
        {
            await connection.SendErrorAsync("REMATCH_NOT_AVAILABLE", message, request.RequestId, ct).ConfigureAwait(false);
            return;
        }
        if (!accept)
        {
            if (challenges.TryGetRoom(originalRoomId, out var oldRoom))
                await SendToPlayersAsync(oldRoom, players, connections, new ServerEventEnvelope<object>
                {
                    Type = "REMATCH_DECLINED",
                    EventId = Guid.NewGuid().ToString("N"),
                    CausationRequestId = request.RequestId,
                    RoomId = originalRoomId,
                    ServerTimeUtc = DateTimeOffset.UtcNow,
                    Payload = new { originalRoomId, declinedBy = session.PlayerId }
                }, ct).ConfigureAwait(false);
            ServerConsoleLog.Info("ĐẤU LẠI", $"{session.DisplayName} từ chối đấu lại phòng {originalRoomId}");
            return;
        }

        if (newRoom is null)
        {
            await connection.SendErrorAsync("REMATCH_NOT_AVAILABLE", "Không tạo được phòng đấu lại.", request.RequestId, ct).ConfigureAwait(false);
            return;
        }
        ServerConsoleLog.Success("ĐẤU LẠI", $"Hai bên đồng ý • phòng mới {newRoom.RoomId} • đã đổi màu quân");
        foreach (var playerId in new[] { newRoom.RedPlayerId, newRoom.BlackPlayerId })
        {
            if (!players.TryGetByPlayerId(playerId, out var participant) ||
                !connections.TryGetConnection(participant.ConnectionId, out var target)) continue;
            try
            {
                await target.SendAsync(RoomMessages.RoomCreated(newRoom, request.RequestId), ct).ConfigureAwait(false);
                var role = playerId == newRoom.RedPlayerId ? "PLAYER_RED" : "PLAYER_BLACK";
                await target.SendAsync(RoomMessages.GameStateSnapshot(newRoom, request.RequestId, role), ct).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                ServerConsoleLog.Warning("ĐẤU LẠI", $"Không thể gửi ván mới tới {playerId}: {exception.Message}");
            }
        }
    }

    public static async Task RematchCancelAsync(
        RequestEnvelope<JsonElement> request,
        ClientConnectionHandler connection,
        ChallengeManager challenges,
        PlayerSessionDirectory players,
        IConnectionRegistry connections,
        CancellationToken ct)
    {
        if (!players.TryGetByConnectionId(connection.ConnectionId, out var session) ||
            !players.ValidateSessionToken(session, request.SessionToken))
        {
            await connection.SendErrorAsync(ErrorCodes.UNAUTHENTICATED, "Bạn cần đăng nhập.", request.RequestId, ct).ConfigureAwait(false);
            return;
        }
        var originalRoomId = request.RoomId ?? ReadString(request.Payload, "originalRoomId");
        if (string.IsNullOrWhiteSpace(originalRoomId))
        {
            await connection.SendErrorAsync(ErrorCodes.INVALID_MESSAGE_SCHEMA,
                "REMATCH_CANCEL cần originalRoomId.", request.RequestId, ct).ConfigureAwait(false);
            return;
        }
        if (!challenges.TryCancelRematch(originalRoomId, session.PlayerId, out _, out var message))
        {
            await connection.SendErrorAsync("REMATCH_NOT_AVAILABLE", message, request.RequestId, ct).ConfigureAwait(false);
            return;
        }
        if (!challenges.TryGetRoom(originalRoomId, out var room)) return;
        await SendToPlayersAsync(room, players, connections, new ServerEventEnvelope<object>
        {
            Type = "REMATCH_CANCELLED",
            EventId = Guid.NewGuid().ToString("N"),
            CausationRequestId = request.RequestId,
            RoomId = originalRoomId,
            ServerTimeUtc = DateTimeOffset.UtcNow,
            Payload = new { originalRoomId, cancelledBy = session.PlayerId }
        }, ct).ConfigureAwait(false);
    }

    private static async Task SendToPlayersAsync(
        GameRoom room,
        PlayerSessionDirectory players,
        IConnectionRegistry connections,
        ServerEventEnvelope<object> message,
        CancellationToken ct)
    {
        foreach (var playerId in new[] { room.RedPlayerId, room.BlackPlayerId })
        {
            if (!players.TryGetByPlayerId(playerId, out var player) ||
                !connections.TryGetConnection(player.ConnectionId, out var target)) continue;
            try { await target.SendAsync(message, ct).ConfigureAwait(false); }
            catch (Exception exception)
            {
                ServerConsoleLog.Warning("ĐẤU LẠI", $"Không thể gửi thông báo tới {playerId}: {exception.Message}");
            }
        }
    }

    private static bool TryResolveTerminalPlayerRoom(
        RequestEnvelope<JsonElement> request,
        ClientConnectionHandler connection,
        ChallengeManager challenges,
        PlayerSessionDirectory players,
        out PlayerSession session,
        out GameRoom room,
        out string error)
    {
        session = null!;
        room = null!;
        if (!players.TryGetByConnectionId(connection.ConnectionId, out session) ||
            !players.ValidateSessionToken(session, request.SessionToken))
        {
            error = ErrorCodes.UNAUTHENTICATED;
            return false;
        }
        var roomId = request.RoomId ?? ReadString(request.Payload, "originalRoomId");
        if (roomId is null || !challenges.TryGetRoom(roomId, out var resolvedRoom))
        {
            error = ErrorCodes.ROOM_NOT_FOUND;
            return false;
        }
        room = resolvedRoom;
        if (!room.HasPlayer(session.PlayerId))
        {
            error = ErrorCodes.SPECTATOR_ACTION_NOT_ALLOWED;
            return false;
        }
        if (!room.IsTerminal)
        {
            error = "GAME_NOT_FINISHED";
            return false;
        }
        error = ErrorCodes.OK;
        return true;
    }

    private static bool TryResolvePlayerRoom(
        RequestEnvelope<JsonElement> request,
        ClientConnectionHandler connection,
        ChallengeManager challenges,
        PlayerSessionDirectory players,
        out PlayerSession session,
        out GameRoom room,
        out string error)
    {
        session = null!; room = null!;
        if (!players.TryGetByConnectionId(connection.ConnectionId, out session) ||
            !players.ValidateSessionToken(session, request.SessionToken)) { error = ErrorCodes.UNAUTHENTICATED; return false; }
        var roomId = request.RoomId ?? session.RoomId;
        if (roomId is null || !challenges.TryGetRoom(roomId, out var resolvedRoom)) { error = ErrorCodes.ROOM_NOT_FOUND; return false; }
        room = resolvedRoom;
        if (!room.HasPlayer(session.PlayerId)) { error = ErrorCodes.SPECTATOR_ACTION_NOT_ALLOWED; return false; }
        if (room.IsTerminal) { error = ErrorCodes.GAME_ALREADY_FINISHED; return false; }
        error = ErrorCodes.OK; return true;
    }

    private static string? ReadString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;

    private static void EnsureMatch(GamePersistenceService persistence, GameRoom room)
    {
        if (persistence.GetMatch(room.RoomId) is null)
            persistence.CreateMatch(room.RoomId, room.RedPlayerId, room.BlackPlayerId, room.RoomId, room.RuleProfileId, room.Clock.Profile.Id);
    }

    private static void TryPersistCompletion(GamePersistenceService persistence, GameRoom room, GameResult result)
    {
        try
        {
            EnsureMatch(persistence, room);
            persistence.CompleteMatch(room.RoomId, result.ResultType, result.EndReason,
                result.WinnerSide?.ToString().ToUpperInvariant(), result.FinalRevision, result.EndedAtUtc.UtcDateTime);
        }
        catch (Exception exception)
        {
            ServerConsoleLog.Warning("LƯU TRẬN", $"Không thể lưu kết quả phòng {room.RoomId}: {exception.Message}");
        }
    }
}
