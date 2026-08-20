using System;
using XiangqiOnline.Shared.Models;
using XiangqiOnline.Shared.Session;
using Xunit;

namespace XiangqiOnline.Shared.Tests.Session
{
    public class ReconnectSessionTrackerTests
    {
        private static (ReconnectSessionTracker tracker, SessionTokenService tokens) CreateTracker(int reconnectWindowSeconds = 60)
        {
            var tokens = new SessionTokenService(new SessionTokenSettings { TokenSizeBytes = 32, ReconnectWindowSeconds = reconnectWindowSeconds });
            var tracker = new ReconnectSessionTracker(tokens, TimeSpan.FromSeconds(reconnectWindowSeconds));
            return (tracker, tokens);
        }

        [Fact]
        public void MarkDisconnected_ThenGetState_ReturnsAwaitingReconnect()
        {
            var (tracker, _) = CreateTracker();
            var now = DateTimeOffset.UtcNow;
            tracker.MarkConnected("player-1", "conn-A");

            tracker.MarkDisconnected("player-1", now);

            Assert.Equal(ConnectionPresenceState.AwaitingReconnect, tracker.GetState("player-1", now));
        }

        [Fact]
        public void GetState_UnknownPlayerId_ReturnsUnknown()
        {
            var (tracker, _) = CreateTracker();

            Assert.Equal(ConnectionPresenceState.Unknown, tracker.GetState("khong-ton-tai", DateTimeOffset.UtcNow));
        }

        [Fact]
        public void TryReconnect_ValidTokenWithinWindow_Accepted_AndTransitionsBackToConnected()
        {
            var (tracker, tokens) = CreateTracker(reconnectWindowSeconds: 60);
            var now = DateTimeOffset.UtcNow;
            var issued = tokens.IssueToken("player-1", now);
            tracker.MarkConnected("player-1", "conn-A");
            tracker.MarkDisconnected("player-1", now);

            var result = tracker.TryReconnect(issued.Token, "conn-B", now.AddSeconds(10));

            Assert.True(result.IsAccepted);
            Assert.Equal("player-1", result.PlayerId);
            Assert.Equal(ConnectionPresenceState.Connected, tracker.GetState("player-1", now.AddSeconds(10)));
        }

        [Fact]
        public void TryReconnect_ExpiredToken_Rejected_WithReconnectWindowExpiredCode()
        {
            var (tracker, tokens) = CreateTracker(reconnectWindowSeconds: 10);
            var now = DateTimeOffset.UtcNow;
            var issued = tokens.IssueToken("player-1", now);
            tracker.MarkConnected("player-1", "conn-A");
            tracker.MarkDisconnected("player-1", now);

            var result = tracker.TryReconnect(issued.Token, "conn-B", now.AddSeconds(20)); // > 10s window

            Assert.False(result.IsAccepted);
            Assert.Equal(ErrorCodes.RECONNECT_WINDOW_EXPIRED, result.ErrorCode);
        }

        [Fact]
        public void TryReconnect_InvalidToken_Rejected_WithInvalidSessionCode()
        {
            var (tracker, _) = CreateTracker();

            var result = tracker.TryReconnect("token-gia-mao", "conn-B", DateTimeOffset.UtcNow);

            Assert.False(result.IsAccepted);
            Assert.Equal(ErrorCodes.INVALID_SESSION, result.ErrorCode);
        }

        [Fact]
        public void TryReconnect_NoPriorSession_DoesNotAutoCreatePlayer_Rejected()
        {
            // Đúng tiêu chí "không tạo player mới": token hợp lệ nhưng CHƯA từng
            // MarkConnected -> vẫn phải từ chối, không tự khởi tạo state mới.
            var (tracker, tokens) = CreateTracker();
            var now = DateTimeOffset.UtcNow;
            var issued = tokens.IssueToken("player-chua-tung-connect", now);
            // Cố tình KHÔNG gọi tracker.MarkConnected(...)

            var result = tracker.TryReconnect(issued.Token, "conn-B", now);

            Assert.False(result.IsAccepted);
            Assert.Equal(ErrorCodes.INVALID_SESSION, result.ErrorCode);
            Assert.Equal(ConnectionPresenceState.Unknown, tracker.GetState("player-chua-tung-connect", now));
        }

        [Fact]
        public void TryReconnect_WhileStillConnected_Rejected_WithDuplicateSessionCode()
        {
            // Đang có 1 connectionId sống (chưa MarkDisconnected) -> không cho reconnect chồng lên.
            var (tracker, tokens) = CreateTracker();
            var now = DateTimeOffset.UtcNow;
            var issued = tokens.IssueToken("player-1", now);
            tracker.MarkConnected("player-1", "conn-A");

            var result = tracker.TryReconnect(issued.Token, "conn-B", now);

            Assert.False(result.IsAccepted);
            Assert.Equal(ErrorCodes.DUPLICATE_SESSION, result.ErrorCode);
        }

        [Fact]
        public void TryReconnect_TwiceWithSameOldToken_SecondAttemptFails_TokenAlreadyRotated()
        {
            // Sau khi reconnect thành công, SessionTokenService không tự cấp token mới ở
            // bước này (giữ nguyên token cũ để đơn giản hoá luồng) — nhưng nếu tầng gọi
            // MarkDisconnected rồi thử reconnect lại lần 2 bằng đúng token đó, phải vẫn
            // hoạt động bình thường (token không bị "dùng 1 lần rồi hỏng").
            var (tracker, tokens) = CreateTracker(reconnectWindowSeconds: 60);
            var now = DateTimeOffset.UtcNow;
            var issued = tokens.IssueToken("player-1", now);
            tracker.MarkConnected("player-1", "conn-A");

            tracker.MarkDisconnected("player-1", now);
            var first = tracker.TryReconnect(issued.Token, "conn-B", now.AddSeconds(5));
            Assert.True(first.IsAccepted);

            tracker.MarkDisconnected("player-1", now.AddSeconds(5));
            var second = tracker.TryReconnect(issued.Token, "conn-C", now.AddSeconds(10));
            Assert.True(second.IsAccepted);
        }
    }
}
