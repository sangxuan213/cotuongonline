using System;
using System.Collections.Generic;
using XiangqiOnline.Shared.Models;

namespace XiangqiOnline.Shared.Session
{
    /// <summary>
    /// P3-TV1-D2 deliverable "Server reconnect flow": theo dõi trạng thái kết nối của
    /// từng playerId qua các lần socket chết/sống, tách biệt hoàn toàn khỏi domain
    /// game/room (TV2/TV3) — chỉ trả lời đúng 1 câu hỏi: "sessionToken này, tại thời
    /// điểm này, có được phép NỐI LẠI vào playerId nào không".
    ///
    /// Không tự tạo player mới (đúng tiêu chí nghiệm thu): TryReconnect chỉ cho phép
    /// nối lại vào 1 playerId ĐÃ TỪNG MarkConnected trước đó; không có bản ghi -> từ
    /// chối thẳng, không bao giờ tạo state mới từ hư không.
    ///
    /// Tầng gọi (Server, TV2) chịu trách nhiệm: (1) gọi MarkDisconnected khi
    /// ClientConnectionHandler phát hiện mất kết nối, (2) gọi TryReconnect khi nhận
    /// RECONNECT_REQUEST, (3) tự kiểm tra seat/room dựa trên PlayerId trả về — việc đó
    /// thuộc domain game/room, KHÔNG nằm trong lớp này.
    /// </summary>
    public sealed class ReconnectSessionTracker
    {
        private sealed class Entry
        {
            public required string ConnectionId { get; set; }
            public required ConnectionPresenceState State { get; set; }
            public DateTimeOffset? ReconnectDeadlineUtc { get; set; }
        }

        private readonly SessionTokenService _tokens;
        private readonly TimeSpan _reconnectWindow;
        private readonly Dictionary<string, Entry> _byPlayerId = new(StringComparer.Ordinal);
        private readonly Lock _gate = new();

        public ReconnectSessionTracker(SessionTokenService tokens, TimeSpan reconnectWindow)
        {
            _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
            if (reconnectWindow <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(reconnectWindow), "Phải > 0.");
            _reconnectWindow = reconnectWindow;
        }

        /// <summary>Gọi lúc login thành công (lần đầu) hoặc ngay sau khi TryReconnect chấp nhận.</summary>
        public void MarkConnected(string playerId, string connectionId)
        {
            if (string.IsNullOrWhiteSpace(playerId))
                throw new ArgumentException("playerId không được rỗng.", nameof(playerId));

            lock (_gate)
            {
                _byPlayerId[playerId] = new Entry { ConnectionId = connectionId, State = ConnectionPresenceState.Connected };
            }
        }

        /// <summary>
        /// Gọi khi ClientConnectionHandler phát hiện socket chết. GIỮ NGUYÊN session
        /// (không xoá) — chỉ chuyển sang chờ reconnect, mở deadline tính từ "now".
        /// Không làm gì nếu playerId chưa từng MarkConnected.
        /// </summary>
        public void MarkDisconnected(string playerId, DateTimeOffset now)
        {
            lock (_gate)
            {
                if (!_byPlayerId.TryGetValue(playerId, out var entry)) return;
                entry.State = ConnectionPresenceState.AwaitingReconnect;
                entry.ReconnectDeadlineUtc = now + _reconnectWindow;
            }
        }

        /// <summary>Trạng thái hiện tại — tự động chuyển AwaitingReconnect -> Expired nếu đã quá deadline.</summary>
        public ConnectionPresenceState GetState(string playerId, DateTimeOffset now)
        {
            lock (_gate)
            {
                if (!_byPlayerId.TryGetValue(playerId, out var entry)) return ConnectionPresenceState.Unknown;

                if (entry.State == ConnectionPresenceState.AwaitingReconnect &&
                    entry.ReconnectDeadlineUtc is { } deadline && deadline <= now)
                {
                    entry.State = ConnectionPresenceState.Expired;
                }

                return entry.State;
            }
        }

        /// <summary>
        /// Xử lý 1 RECONNECT_REQUEST. Trả về Accepted(playerId) hoặc
        /// Rejected(errorCode) — errorCode dùng đúng hằng số trong
        /// <see cref="ErrorCodes"/> để tầng Server map thẳng ra ERROR_RESPONSE, không
        /// cần dịch lại.
        /// </summary>
        public ReconnectAttemptResult TryReconnect(string? sessionToken, string newConnectionId, DateTimeOffset now)
        {
            var validation = _tokens.ValidateSessionToken(sessionToken, now);
            if (validation.Outcome == SessionTokenValidationOutcome.Expired)
                return ReconnectAttemptResult.Rejected(ErrorCodes.RECONNECT_WINDOW_EXPIRED);
            if (validation.Outcome != SessionTokenValidationOutcome.Valid)
                return ReconnectAttemptResult.Rejected(ErrorCodes.INVALID_SESSION);

            var playerId = validation.PlayerId!;

            lock (_gate)
            {
                // Không có bản ghi cho playerId này -> KHÔNG tự tạo mới, từ chối thẳng.
                if (!_byPlayerId.TryGetValue(playerId, out var entry))
                    return ReconnectAttemptResult.Rejected(ErrorCodes.INVALID_SESSION);

                if (entry.State == ConnectionPresenceState.Connected)
                    return ReconnectAttemptResult.Rejected(ErrorCodes.DUPLICATE_SESSION); // đang có kết nối sống, không cho chồng lên

                if (entry.State != ConnectionPresenceState.AwaitingReconnect)
                    return ReconnectAttemptResult.Rejected(ErrorCodes.RECONNECT_WINDOW_EXPIRED); // Expired hoặc trạng thái lạ

                if (entry.ReconnectDeadlineUtc is { } deadline && deadline <= now)
                {
                    entry.State = ConnectionPresenceState.Expired;
                    return ReconnectAttemptResult.Rejected(ErrorCodes.RECONNECT_WINDOW_EXPIRED);
                }

                entry.ConnectionId = newConnectionId;
                entry.State = ConnectionPresenceState.Connected;
                entry.ReconnectDeadlineUtc = null;
                return ReconnectAttemptResult.Accepted(playerId);
            }
        }
    }
}
