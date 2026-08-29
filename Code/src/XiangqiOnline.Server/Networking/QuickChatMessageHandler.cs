using System.Collections.Concurrent;
using System.Text.Json;
using XiangqiOnline.Server.Lobby;
using XiangqiOnline.Shared.Models;
using XiangqiOnline.Shared.Protocol;

namespace XiangqiOnline.Server.Networking;

public static class QuickChatMessageHandler
{
    private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(2);
    private static readonly ConcurrentDictionary<string, DateTimeOffset> LastSentAt = new(StringComparer.Ordinal);
    private static readonly IReadOnlyDictionary<string, string> Messages = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["HELLO"] = "Chào bạn!",
        ["GOOD_MOVE"] = "Nước hay!",
        ["THANKS"] = "Cảm ơn nhé!",
        ["THINKING"] = "Để tôi nghĩ...",
        ["GOOD_LUCK"] = "Chúc may mắn!",
        ["GOOD_GAME"] = "Ván hay lắm!",
        ["SMILE"] = "Tuyệt quá!",
        ["SURPRISED"] = "Bất ngờ thật!",
        ["CHALLENGE"] = "🔥 Tới công chuyện rồi!",
        ["CHECK"] = "⚔️ Cẩn thận chiếu tướng!",
        ["PRESSURE"] = "😎 Áp lực chưa?",
        ["COMEBACK"] = "🐉 Tôi sẽ lật ngược thế cờ!"
    };

    public static async Task HandleAsync(
        RequestEnvelope<JsonElement> request,
        ClientConnectionHandler connection,
        PlayerSessionDirectory players,
        ChallengeManager challenges,
        IConnectionRegistry connections,
        CancellationToken cancellationToken)
    {
        if (!players.TryGetByConnectionId(connection.ConnectionId, out var player) ||
            !players.ValidateSessionToken(player, request.SessionToken))
        {
            await connection.SendErrorAsync(ErrorCodes.UNAUTHENTICATED, "Bạn cần đăng nhập để trò chuyện.", request.RequestId, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (string.IsNullOrWhiteSpace(request.RoomId) || !challenges.TryGetRoom(request.RoomId, out var room))
        {
            await connection.SendErrorAsync(ErrorCodes.ROOM_NOT_FOUND, "Không tìm thấy phòng trò chuyện.", request.RequestId, cancellationToken).ConfigureAwait(false);
            return;
        }

        var isSpectator = room!.SpectatorConnectionIds.Contains(connection.ConnectionId, StringComparer.Ordinal);
        if (!room.HasPlayer(player.PlayerId) && !isSpectator)
        {
            await connection.SendErrorAsync(ErrorCodes.NOT_ROOM_MEMBER, "Bạn không thuộc phòng này.", request.RequestId, cancellationToken).ConfigureAwait(false);
            return;
        }

        var code = request.Payload.TryGetProperty("code", out var codeNode) && codeNode.ValueKind == JsonValueKind.String
            ? codeNode.GetString()?.Trim().ToUpperInvariant()
            : null;
        string? text;
        if (!string.IsNullOrWhiteSpace(code) && Messages.TryGetValue(code, out var preset))
        {
            text = preset;
        }
        else if (request.Payload.TryGetProperty("text", out var textNode) && textNode.ValueKind == JsonValueKind.String)
        {
            code = "TEXT";
            text = NormalizeText(textNode.GetString());
        }
        else
        {
            text = null;
        }
        if (string.IsNullOrWhiteSpace(text))
        {
            await connection.SendErrorAsync(ErrorCodes.INVALID_MESSAGE_SCHEMA, "Tin nhắn phải có từ 1 đến 200 ký tự.", request.RequestId, cancellationToken).ConfigureAwait(false);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (LastSentAt.TryGetValue(player.PlayerId, out var previous) && now - previous < Cooldown)
        {
            await connection.SendErrorAsync(ErrorCodes.RATE_LIMITED, "Vui lòng chờ 2 giây trước khi gửi tiếp.", request.RequestId, cancellationToken).ConfigureAwait(false);
            return;
        }
        LastSentAt[player.PlayerId] = now;
        CleanupOldEntries(now);

        await RoomEventBroadcaster.BroadcastAsync(room, players, connections, new ServerEventEnvelope<object>
        {
            Type = "QUICK_CHAT_RECEIVED",
            EventId = Guid.NewGuid().ToString("N"),
            CausationRequestId = request.RequestId,
            RoomId = room.RoomId,
            ServerTimeUtc = now,
            Payload = new
            {
                messageId = Guid.NewGuid().ToString("N"),
                roomId = room.RoomId,
                senderPlayerId = player.PlayerId,
                senderDisplayName = player.DisplayName,
                code,
                text,
                isSpectator,
                sentAtUtc = now
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    private static void CleanupOldEntries(DateTimeOffset now)
    {
        if (LastSentAt.Count < 2048) return;
        foreach (var entry in LastSentAt.Where(entry => now - entry.Value > TimeSpan.FromMinutes(10)))
            LastSentAt.TryRemove(entry.Key, out _);
    }

    private static string? NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var cleaned = new string(value.Trim().Where(character => !char.IsControl(character) || character is '\r' or '\n' or '\t').ToArray());
        return cleaned.Length is > 0 and <= 200 ? cleaned : null;
    }
}
