using XiangqiOnline.Server.Lobby;
using XiangqiOnline.Shared.Protocol;

namespace XiangqiOnline.Server.Networking
{
    public static class RoomMessages
    {
        public static ServerEventEnvelope<object> RoomCreated(GameRoom room, string? causationRequestId = null) => new()
        {
            Type = "ROOM_CREATED",
            EventId = Guid.NewGuid().ToString("N"),
            CausationRequestId = causationRequestId,
            RoomId = room.RoomId,
            ServerTimeUtc = DateTimeOffset.UtcNow,
            Payload = new { roomId = room.RoomId }
        };

        public static ServerEventEnvelope<object> GameStateSnapshot(
            GameRoom room,
            string? causationRequestId = null,
            string viewerRole = "PLAYER")
        {
            var pieces = room.Board.GetActivePieces()
                .Select(piece => new
                {
                    pieceId = piece.Id,
                    side = piece.Side.ToString().ToUpperInvariant(),
                    type = piece.Type.ToString().ToUpperInvariant(),
                    x = piece.Position.X,
                    y = piece.Position.Y,
                    captured = false
                })
                .OrderBy(piece => piece.pieceId, StringComparer.Ordinal)
                .ToArray();

            return new ServerEventEnvelope<object>
            {
                Type = "GAME_STATE_SNAPSHOT",
                EventId = Guid.NewGuid().ToString("N"),
                CausationRequestId = causationRequestId,
                RoomId = room.RoomId,
                Revision = room.Revision,
                ServerTimeUtc = DateTimeOffset.UtcNow,
                Payload = new
                {
                    roomId = room.RoomId,
                    revision = room.Revision,
                    currentTurn = room.CurrentTurn.ToString().ToUpperInvariant(),
                    status = room.Status.ToString(),
                    pieces,
                    viewerRole,
                    clocks = room.Clock.Snapshot(),
                    mustVarySide = room.MustVarySide?.ToString().ToUpperInvariant(),
                    spectatorCount = room.SpectatorConnectionIds.Count,
                    recentMoves = room.Moves.TakeLast(50).Select(move => new
                    {
                        move.Revision,
                        side = move.Side.ToString().ToUpperInvariant(),
                        move.PieceId,
                        from = new { x = move.From.X, y = move.From.Y },
                        to = new { x = move.To.X, y = move.To.Y },
                        move.CapturedPieceId,
                        move.Classification,
                        move.IsCheck
                    }).ToArray(),
                    result = room.Result
                }
            };
        }

        public static ServerEventEnvelope<object> GameEnded(GameRoom room, string? causationRequestId = null) => new()
        {
            Type = "GAME_ENDED",
            EventId = Guid.NewGuid().ToString("N"),
            CausationRequestId = causationRequestId,
            RoomId = room.RoomId,
            Revision = room.Revision,
            ServerTimeUtc = DateTimeOffset.UtcNow,
            Payload = new { finalResult = room.Result, finalSnapshot = GameStateSnapshot(room).Payload }
        };

        public static ServerEventEnvelope<object> ClockSync(GameRoom room, string? causationRequestId = null) => new()
        {
            Type = "CLOCK_SYNC",
            EventId = Guid.NewGuid().ToString("N"),
            CausationRequestId = causationRequestId,
            RoomId = room.RoomId,
            Revision = room.Revision,
            ServerTimeUtc = DateTimeOffset.UtcNow,
            Payload = new { clockState = room.Clock.Snapshot(), serverAnchor = DateTimeOffset.UtcNow }
        };

        public static ServerEventEnvelope<object> RepetitionWarning(GameRoom room, string? causationRequestId = null) => new()
        {
            Type = "REPETITION_WARNING",
            EventId = Guid.NewGuid().ToString("N"),
            CausationRequestId = causationRequestId,
            RoomId = room.RoomId,
            Revision = room.Revision,
            ServerTimeUtc = DateTimeOffset.UtcNow,
            Payload = new
            {
                mustVarySide = room.MustVarySide?.ToString().ToUpperInvariant(),
                cycleSignature = room.RepetitionCycleSignature
            }
        };
    }
}
