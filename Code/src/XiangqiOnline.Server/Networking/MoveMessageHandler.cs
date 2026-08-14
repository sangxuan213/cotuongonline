using System.Text.Json;
using XiangqiOnline.Persistence.Services;
using XiangqiOnline.Server.Lobby;
using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;
using XiangqiOnline.Shared.Protocol;

namespace XiangqiOnline.Server.Networking;

public static class MoveMessageHandler
{
    private const string RevisionMismatch = "REVISION_MISMATCH";

    public static async Task HandleAsync(
        RequestEnvelope<JsonElement> request,
        ClientConnectionHandler connection,
        PlayerSessionDirectory players,
        ChallengeManager challenges,
        IConnectionRegistry connections,
        GamePersistenceService persistence,
        CancellationToken ct)
    {
        if (!players.TryGetByConnectionId(connection.ConnectionId, out var requester))
        {
            await RejectAsync(connection, request, ErrorCodes.INVALID_SESSION, "Player is not logged in.", 0, ct).ConfigureAwait(false);
            return;
        }

        if (string.IsNullOrWhiteSpace(request.RoomId) ||
            !challenges.TryGetRoom(request.RoomId, out var room) ||
            room is null)
        {
            await RejectAsync(connection, request, ErrorCodes.INVALID_SESSION, "Room was not found.", 0, ct).ConfigureAwait(false);
            return;
        }

        if (room.Status != GameRoomStatus.PLAYING)
        {
            await RejectAsync(connection, request, ErrorCodes.GAME_NOT_ACTIVE, "Room is not playing.", room.Revision, ct).ConfigureAwait(false);
            return;
        }

        if (!room.HasPlayer(requester.PlayerId))
        {
            await RejectAsync(connection, request, ErrorCodes.INVALID_SESSION, "Player is not a member of this room.", room.Revision, ct).ConfigureAwait(false);
            return;
        }

        var requesterSide = room.GetSideForPlayer(requester.PlayerId);
        if (requesterSide != room.CurrentTurn)
        {
            await RejectAsync(connection, request, ErrorCodes.NOT_YOUR_TURN, "It is not this player's turn.", room.Revision, ct).ConfigureAwait(false);
            return;
        }

        if (!TryParseIntent(request.Payload, out var intent))
        {
            await RejectAsync(connection, request, ErrorCodes.INTERNAL_SERVER_ERROR, "MOVE_REQUEST payload is invalid.", room.Revision, ct).ConfigureAwait(false);
            return;
        }

        if (intent.ExpectedRevision != room.Revision)
        {
            await RejectAsync(connection, request, RevisionMismatch, "Expected revision does not match the room revision.", room.Revision, ct).ConfigureAwait(false);
            return;
        }

        var match = persistence.GetMatch(room.RoomId) ?? persistence.CreateMatch(
            room.RoomId,
            room.RedPlayerId,
            room.BlackPlayerId,
            room.RoomId);

        if (match.Revision != room.Revision)
        {
            await RejectAsync(connection, request, RevisionMismatch, "Persistence revision does not match the room revision.", room.Revision, ct).ConfigureAwait(false);
            return;
        }

        var result = persistence.CommitMove(match, room.Board, intent);
        if (!result.IsCommitted || result.Move is null)
        {
            await RejectAsync(
                connection,
                request,
                result.ErrorCode ?? ErrorCodes.INTERNAL_SERVER_ERROR,
                result.Message ?? "Move was not committed.",
                room.Revision,
                ct).ConfigureAwait(false);
            return;
        }

        var nextBoard = room.Board.ApplyMove(intent.From, intent.To);
        var roomRevision = room.CommitRevision(nextBoard);
        if (roomRevision != result.Revision)
            throw new InvalidOperationException("Committed persistence and room revisions diverged.");

        var committed = new ServerEventEnvelope<object>
        {
            Type = "MOVE_COMMITTED",
            EventId = Guid.NewGuid().ToString("N"),
            CausationRequestId = request.RequestId,
            RoomId = room.RoomId,
            Revision = result.Revision,
            ServerTimeUtc = DateTimeOffset.UtcNow,
            Payload = new
            {
                side = result.Move.Side,
                pieceId = result.Move.PieceId,
                from = new { x = result.Move.From.X, y = result.Move.From.Y },
                to = new { x = result.Move.To.X, y = result.Move.To.Y },
                capturedPieceId = result.Move.CapturedPieceId,
                currentTurn = room.CurrentTurn.ToString().ToUpperInvariant()
            }
        };

        foreach (var playerId in new[] { room.RedPlayerId, room.BlackPlayerId })
        {
            if (players.TryGetByPlayerId(playerId, out var player) &&
                connections.TryGetConnection(player.ConnectionId, out var playerConnection))
            {
                await playerConnection.SendAsync(committed, ct).ConfigureAwait(false);
            }
        }
    }

    private static bool TryParseIntent(JsonElement payload, out MoveIntent intent)
    {
        intent = null!;
        try
        {
            if (payload.ValueKind != JsonValueKind.Object ||
                !payload.TryGetProperty("clientMoveId", out var clientMoveIdNode) ||
                string.IsNullOrWhiteSpace(clientMoveIdNode.GetString()) ||
                !payload.TryGetProperty("expectedRevision", out var revisionNode) ||
                !revisionNode.TryGetInt64(out var expectedRevision) ||
                !TryParsePosition(payload, "from", out var from) ||
                !TryParsePosition(payload, "to", out var to))
            {
                return false;
            }

            intent = new MoveIntent(clientMoveIdNode.GetString()!, from, to, expectedRevision);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool TryParsePosition(JsonElement payload, string propertyName, out Position position)
    {
        position = default!;
        if (!payload.TryGetProperty(propertyName, out var node) ||
            node.ValueKind != JsonValueKind.Object ||
            !node.TryGetProperty("x", out var xNode) ||
            !xNode.TryGetInt32(out var x) ||
            !node.TryGetProperty("y", out var yNode) ||
            !yNode.TryGetInt32(out var y))
        {
            return false;
        }

        position = new Position(x, y);
        return true;
    }

    private static Task RejectAsync(
        ClientConnectionHandler connection,
        RequestEnvelope<JsonElement> request,
        string errorCode,
        string message,
        long revision,
        CancellationToken ct)
    {
        return connection.SendAsync(new ServerEventEnvelope<object>
        {
            Type = "MOVE_REJECTED",
            EventId = Guid.NewGuid().ToString("N"),
            CausationRequestId = request.RequestId,
            RoomId = request.RoomId,
            Revision = revision,
            ServerTimeUtc = DateTimeOffset.UtcNow,
            Payload = new { errorCode, message, revision }
        }, ct);
    }
}
