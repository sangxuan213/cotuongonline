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

        public static ServerEventEnvelope<object> GameStateSnapshot(GameRoom room, string? causationRequestId = null)
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
                    pieces
                }
            };
        }
    }
}
