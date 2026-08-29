using System.Text.Json;
using XiangqiOnline.Server.Lobby;
using XiangqiOnline.Shared.Models;
using XiangqiOnline.Shared.Protocol;

namespace XiangqiOnline.Server.Networking;

public static class BotGameMessageHandler
{
    public static async Task HandleAsync(
        RequestEnvelope<JsonElement> request,
        ClientConnectionHandler connection,
        PlayerSessionDirectory players,
        ChallengeManager challenges,
        BotMoveService bots,
        CancellationToken cancellationToken)
    {
        if (!players.TryGetByConnectionId(connection.ConnectionId, out var player) ||
            !players.ValidateSessionToken(player, request.SessionToken))
        {
            await connection.SendErrorAsync(ErrorCodes.UNAUTHENTICATED, "Login is required.", request.RequestId, cancellationToken).ConfigureAwait(false);
            return;
        }
        var text = request.Payload.TryGetProperty("difficulty", out var node) ? node.GetString() : null;
        if (!Enum.TryParse<BotDifficulty>(text, true, out var difficulty))
        {
            await connection.SendErrorAsync(ErrorCodes.INVALID_MESSAGE_SCHEMA, "Difficulty must be EASY, MEDIUM, or HARD.", request.RequestId, cancellationToken).ConfigureAwait(false);
            return;
        }
        if (!challenges.TryCreateBotRoom(player.PlayerId, difficulty.ToString(), DateTimeOffset.UtcNow, out var room, out var error))
        {
            await connection.SendErrorAsync(ErrorCodes.PLAYER_NOT_AVAILABLE, error, request.RequestId, cancellationToken).ConfigureAwait(false);
            return;
        }
        bots.Register(room, difficulty);
        ServerConsoleLog.Success("ĐẤU MÁY", $"{player.DisplayName} bắt đầu mức {difficulty.ToString().ToUpperInvariant()} • phòng {room.RoomId}");
        await connection.SendAsync(RoomMessages.RoomCreated(room, request.RequestId), cancellationToken).ConfigureAwait(false);
        await connection.SendAsync(RoomMessages.GameStateSnapshot(room, request.RequestId, "PLAYER_RED"), cancellationToken).ConfigureAwait(false);
    }
}
