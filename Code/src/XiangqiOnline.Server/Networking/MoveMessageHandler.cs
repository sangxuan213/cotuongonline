using System.Text.Json;
using XiangqiOnline.Persistence.Services;
using XiangqiOnline.RuleEngine.Adjudication;
using XiangqiOnline.RuleEngine.Models;
using XiangqiOnline.Server.Lobby;
using XiangqiOnline.Shared.Enums;
using XiangqiOnline.Shared.Models;
using XiangqiOnline.Shared.Protocol;

namespace XiangqiOnline.Server.Networking;

public static class MoveMessageHandler
{
    public static async Task HandleAsync(
        RequestEnvelope<JsonElement> request,
        ClientConnectionHandler connection,
        PlayerSessionDirectory players,
        ChallengeManager challenges,
        IConnectionRegistry connections,
        GamePersistenceService persistence,
        CancellationToken ct,
        BotMoveService? bots = null)
    {
        if (!players.TryGetByConnectionId(connection.ConnectionId, out var requester))
        {
            await RejectAsync(connection, request, ErrorCodes.INVALID_SESSION, "Player is not logged in.", 0, ct).ConfigureAwait(false);
            return;
        }
        if (!players.ValidateSessionToken(requester, request.SessionToken))
        {
            await RejectAsync(connection, request, ErrorCodes.INVALID_SESSION, "Session token is invalid.", 0, ct).ConfigureAwait(false);
            return;
        }
        if (string.IsNullOrWhiteSpace(request.RoomId) || !challenges.TryGetRoom(request.RoomId, out var room))
        {
            await RejectAsync(connection, request, ErrorCodes.ROOM_NOT_FOUND, "Room was not found.", 0, ct).ConfigureAwait(false);
            return;
        }
        if (!room.HasPlayer(requester.PlayerId))
        {
            var code = room.SpectatorConnectionIds.Contains(connection.ConnectionId)
                ? ErrorCodes.SPECTATOR_ACTION_NOT_ALLOWED : ErrorCodes.NOT_ROOM_MEMBER;
            await RejectAsync(connection, request, code, "Only a seated player can move.", room.Revision, ct).ConfigureAwait(false);
            return;
        }
        if (!TryParseIntent(request.Payload, out var intent))
        {
            await RejectAsync(connection, request, ErrorCodes.INVALID_MESSAGE_SCHEMA, "MOVE_REQUEST payload is invalid.", room.Revision, ct).ConfigureAwait(false);
            return;
        }

        var committedForBot = await room.ExecuteSerializedAsync(async () =>
        {
            if (room.IsTerminal || room.Status != GameRoomStatus.PLAYING)
            {
                await RejectAsync(connection, request, ErrorCodes.GAME_NOT_ACTIVE, "Room is not playing.", room.Revision, ct).ConfigureAwait(false);
                return false;
            }
            var requesterSide = room.GetSideForPlayer(requester.PlayerId);
            if (requesterSide != room.CurrentTurn)
            {
                await RejectAsync(connection, request, ErrorCodes.NOT_YOUR_TURN, "It is not this player's turn.", room.Revision, ct).ConfigureAwait(false);
                return false;
            }
            if (intent.ExpectedRevision != room.Revision)
            {
                await RejectAsync(connection, request, "REVISION_MISMATCH", "Expected revision does not match room revision.", room.Revision, ct).ConfigureAwait(false);
                return false;
            }
            var before = room.Board;
            var movingPiece = before.GetPieceAt(intent.From);
            var capturedPiece = before.GetPieceAt(intent.To);
            XiangqiOnline.Persistence.Models.MatchRecord match;
            try
            {
                match = persistence.GetMatch(room.RoomId) ?? persistence.CreateMatch(
                    room.RoomId, room.RedPlayerId, room.BlackPlayerId, room.RoomId, room.RuleProfileId, room.Clock.Profile.Id);
            }
            catch (Exception ex)
            {
                await RejectAsync(connection, request, ErrorCodes.PERSISTENCE_FAILED,
                    $"Không thể khởi tạo dữ liệu trận đấu: {ex.Message}", room.Revision, ct).ConfigureAwait(false);
                return false;
            }
            if (match.Revision != room.Revision)
            {
                await RejectAsync(connection, request, "REVISION_MISMATCH", "Persistence revision does not match room revision.", room.Revision, ct).ConfigureAwait(false);
                return false;
            }

            if (!room.Clock.TryPrepareMove(requesterSide, out var clockReservation, out var reservedClocks))
            {
                await FinishTimeoutAsync(room, requesterSide, players, connections, persistence, request.RequestId, ct).ConfigureAwait(false);
                await RejectAsync(connection, request, ErrorCodes.TIME_EXPIRED, "Clock expired before the move arrived.", room.Revision, ct).ConfigureAwait(false);
                return false;
            }

            MoveCommitResult result;
            try
            {
                result = persistence.CommitMove(
                    match,
                    before,
                    intent,
                    checked((int)Math.Min(int.MaxValue, reservedClocks.RedRemainingMs)),
                    checked((int)Math.Min(int.MaxValue, reservedClocks.BlackRemainingMs)));
            }
            catch (Exception ex)
            {
                await RejectAsync(connection, request, ErrorCodes.PERSISTENCE_FAILED, ex.Message, room.Revision, ct).ConfigureAwait(false);
                return false;
            }
            if (!result.IsCommitted || result.Move is null || movingPiece is null)
            {
                await RejectAsync(connection, request, result.ErrorCode ?? ErrorCodes.INTERNAL_SERVER_ERROR,
                    result.Message ?? "Move was not committed.", room.Revision, ct).ConfigureAwait(false);
                return false;
            }

            var nextBoard = before.ApplyMove(intent.From, intent.To);
            var termination = new GameTerminationDetector().Evaluate(nextBoard);
            var application = new MoveApplicationResult(
                before, nextBoard, movingPiece, capturedPiece, intent.From, intent.To,
                BoardFingerprint.Hash(before), BoardFingerprint.Hash(nextBoard), termination.IsCheck);
            var classification = new MoveClassifier().Classify(application);
            var clocks = room.Clock.CommitPreparedMove(clockReservation);
            var revision = room.CommitMove(nextBoard, intent.ClientMoveId, movingPiece.Id, intent.From, intent.To,
                capturedPiece?.Id, classification, clocks, DateTimeOffset.UtcNow);
            if (revision != result.Revision)
                throw new InvalidOperationException("Committed persistence and room revisions diverged.");

            var committed = new ServerEventEnvelope<object>
            {
                Type = "MOVE_COMMITTED", EventId = Guid.NewGuid().ToString("N"), CausationRequestId = request.RequestId,
                RoomId = room.RoomId, Revision = revision, ServerTimeUtc = DateTimeOffset.UtcNow,
                Payload = new
                {
                    side = requesterSide.ToString().ToUpperInvariant(), pieceId = movingPiece.Id,
                    from = new { x = intent.From.X, y = intent.From.Y }, to = new { x = intent.To.X, y = intent.To.Y },
                    capturedPieceId = capturedPiece?.Id, currentTurn = room.CurrentTurn.ToString().ToUpperInvariant(),
                    moveClass = classification.Classification.ToString(), isCheck = classification.IsCheck,
                    isCheckmate = termination.IsTerminal && termination.IsCheck,
                    clocks, status = room.Status.ToString()
                }
            };
            await RoomEventBroadcaster.BroadcastAsync(room, players, connections, committed, ct).ConfigureAwait(false);
            ServerConsoleLog.Success("NƯỚC ĐI",
                $"{requester.DisplayName} • {movingPiece.Id} {intent.From.X},{intent.From.Y} → {intent.To.X},{intent.To.Y} • rev {revision}");

            if (termination.IsTerminal)
            {
                var resultType = termination.Winner == SideColor.Red ? "RED_WIN" : "BLACK_WIN";
                await FinishAsync(room, new GameResult(resultType, termination.EndReason!, termination.Winner,
                    DateTimeOffset.UtcNow, room.Revision, $"No legal move; check={termination.IsCheck}."),
                    players, connections, persistence, request.RequestId, ct).ConfigureAwait(false);
                return true;
            }

            var repetition = new RepetitionResolver().Evaluate(room.PositionHistory, room.MustVarySide);
            if (!repetition.IsCycle && room.MustVarySide is not null)
                room.SetRepetitionWarning(null, null);
            else if (repetition.ShouldWarn)
            {
                room.SetRepetitionWarning(repetition.MustVarySide, repetition.CycleSignature);
                await RoomEventBroadcaster.BroadcastAsync(room, players, connections,
                    RoomMessages.RepetitionWarning(room, request.RequestId), ct).ConfigureAwait(false);
            }
            else if (repetition.IsTerminal)
            {
                var type = repetition.Winner switch
                {
                    SideColor.Red => "RED_WIN",
                    SideColor.Black => "BLACK_WIN",
                    _ => "DRAW"
                };
                await FinishAsync(room, new GameResult(type, repetition.EndReason!, repetition.Winner,
                    DateTimeOffset.UtcNow, room.Revision, repetition.Explanation),
                    players, connections, persistence, request.RequestId, ct).ConfigureAwait(false);
            }
            return true;
        }, ct).ConfigureAwait(false);
        if (committedForBot && bots is not null)
            await bots.PlayIfNeededAsync(room, CancellationToken.None).ConfigureAwait(false);
    }

    private static Task FinishTimeoutAsync(
        GameRoom room, SideColor expired, PlayerSessionDirectory players, IConnectionRegistry connections,
        GamePersistenceService persistence, string? requestId, CancellationToken ct)
    {
        var winner = expired == SideColor.Red ? SideColor.Black : SideColor.Red;
        return FinishAsync(room, new GameResult(winner == SideColor.Red ? "RED_WIN" : "BLACK_WIN", "TIMEOUT", winner,
            DateTimeOffset.UtcNow, room.Revision, $"{expired} clock expired."), players, connections, persistence, requestId, ct);
    }

    private static async Task FinishAsync(
        GameRoom room, GameResult finalResult, PlayerSessionDirectory players, IConnectionRegistry connections,
        GamePersistenceService persistence, string? requestId, CancellationToken ct)
    {
        if (!room.TryFinish(finalResult)) return;
        try
        {
            if (persistence.GetMatch(room.RoomId) is null)
                persistence.CreateMatch(room.RoomId, room.RedPlayerId, room.BlackPlayerId, room.RoomId, room.RuleProfileId, room.Clock.Profile.Id);
            persistence.CompleteMatch(room.RoomId, finalResult.ResultType, finalResult.EndReason,
                finalResult.WinnerSide?.ToString().ToUpperInvariant(), finalResult.FinalRevision, finalResult.EndedAtUtc.UtcDateTime);
        }
        catch
        {
            // The authoritative room must still terminate and release both players.
        }
        players.LeaveRoom(room.RedPlayerId);
        players.LeaveRoom(room.BlackPlayerId);
        await RoomEventBroadcaster.BroadcastAsync(room, players, connections, RoomMessages.GameEnded(room, requestId), ct).ConfigureAwait(false);
    }

    private static bool TryParseIntent(JsonElement payload, out MoveIntent intent)
    {
        intent = null!;
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("clientMoveId", out var id) || string.IsNullOrWhiteSpace(id.GetString()) ||
            !payload.TryGetProperty("expectedRevision", out var rev) || !rev.TryGetInt64(out var revision) ||
            !TryPosition(payload, "from", out var from) || !TryPosition(payload, "to", out var to)) return false;
        intent = new MoveIntent(id.GetString()!, from, to, revision);
        return true;
    }

    private static bool TryPosition(JsonElement payload, string name, out Position position)
    {
        position = default!;
        if (!payload.TryGetProperty(name, out var node) || node.ValueKind != JsonValueKind.Object ||
            !node.TryGetProperty("x", out var x) || !x.TryGetInt32(out var px) ||
            !node.TryGetProperty("y", out var y) || !y.TryGetInt32(out var py)) return false;
        position = new Position(px, py);
        return true;
    }

    private static Task RejectAsync(
        ClientConnectionHandler connection, RequestEnvelope<JsonElement> request,
        string errorCode, string message, long revision, CancellationToken ct) =>
        connection.SendAsync(new ServerEventEnvelope<object>
        {
            Type = "MOVE_REJECTED", EventId = Guid.NewGuid().ToString("N"), CausationRequestId = request.RequestId,
            RoomId = request.RoomId, Revision = revision, ServerTimeUtc = DateTimeOffset.UtcNow,
            Payload = new { errorCode, message, revision }
        }, ct);
}
