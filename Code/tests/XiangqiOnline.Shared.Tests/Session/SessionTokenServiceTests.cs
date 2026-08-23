using System;
using XiangqiOnline.Shared.Session;
using Xunit;

namespace XiangqiOnline.Shared.Tests.Session
{
    public class SessionTokenServiceTests
    {
        private static SessionTokenService CreateService(int reconnectWindowSeconds = 60) =>
            new(new SessionTokenSettings { TokenSizeBytes = 32, ReconnectWindowSeconds = reconnectWindowSeconds });

        [Fact]
        public void IssueToken_ProducesAtLeast256BitsOfRandomness()
        {
            var service = CreateService();
            var now = DateTimeOffset.UtcNow;

            var issued = service.IssueToken("player-1", now);

            // base64url của 32 byte (256 bit) -> độ dài chuỗi tối thiểu tương ứng (không padding).
            var decodedLength = System.Buffers.Text.Base64Url.GetMaxDecodedLength(issued.Token.Length);
            Assert.True(decodedLength >= 32);
        }

        [Fact]
        public void IssueToken_TwoCalls_ProduceDifferentTokens()
        {
            var service = CreateService();
            var now = DateTimeOffset.UtcNow;

            var a = service.IssueToken("player-1", now);
            var b = service.IssueToken("player-2", now);

            Assert.NotEqual(a.Token, b.Token);
        }

        [Fact]
        public void ValidateSessionToken_JustIssuedToken_ReturnsValidWithCorrectPlayerId()
        {
            var service = CreateService();
            var now = DateTimeOffset.UtcNow;
            var issued = service.IssueToken("player-1", now);

            var result = service.ValidateSessionToken(issued.Token, now);

            Assert.Equal(SessionTokenValidationOutcome.Valid, result.Outcome);
            Assert.Equal("player-1", result.PlayerId);
        }

        [Fact]
        public void ValidateSessionToken_UnknownToken_ReturnsInvalid()
        {
            var service = CreateService();

            var result = service.ValidateSessionToken("token-khong-ton-tai", DateTimeOffset.UtcNow);

            Assert.Equal(SessionTokenValidationOutcome.Invalid, result.Outcome);
            Assert.Null(result.PlayerId);
        }

        [Fact]
        public void ValidateSessionToken_NullOrEmpty_ReturnsInvalid_NotException()
        {
            var service = CreateService();

            Assert.Equal(SessionTokenValidationOutcome.Invalid, service.ValidateSessionToken(null, DateTimeOffset.UtcNow).Outcome);
            Assert.Equal(SessionTokenValidationOutcome.Invalid, service.ValidateSessionToken("", DateTimeOffset.UtcNow).Outcome);
        }

        [Fact]
        public void ValidateSessionToken_AfterReconnectWindowExpires_ReturnsExpired()
        {
            var service = CreateService(reconnectWindowSeconds: 60);
            var issuedAt = DateTimeOffset.UtcNow;
            var issued = service.IssueToken("player-1", issuedAt);

            var result = service.ValidateSessionToken(issued.Token, issuedAt.AddSeconds(61));

            Assert.Equal(SessionTokenValidationOutcome.Expired, result.Outcome);
        }

        [Fact]
        public void IssueToken_CalledTwiceForSamePlayer_InvalidatesOldToken_DuplicateSessionPolicy()
        {
            // "Rotate khi login mới" + chính sách duplicate session: tại 1 thời điểm,
            // 1 playerId chỉ có đúng 1 token còn hiệu lực.
            var service = CreateService();
            var now = DateTimeOffset.UtcNow;

            var first = service.IssueToken("player-1", now);
            var second = service.IssueToken("player-1", now); // login lần 2 cho CÙNG player

            var firstResult = service.ValidateSessionToken(first.Token, now);
            var secondResult = service.ValidateSessionToken(second.Token, now);

            Assert.Equal(SessionTokenValidationOutcome.Invalid, firstResult.Outcome); // token cũ đã chết
            Assert.Equal(SessionTokenValidationOutcome.Valid, secondResult.Outcome);
        }

        [Fact]
        public void ExtendReconnectWindow_PushesExpiryForward_TokenStillValidLater()
        {
            var service = CreateService(reconnectWindowSeconds: 10);
            var issuedAt = DateTimeOffset.UtcNow;
            var issued = service.IssueToken("player-1", issuedAt);

            // Giả lập: 8s sau, phát hiện mất kết nối -> gia hạn cửa sổ reconnect từ đây.
            var disconnectedAt = issuedAt.AddSeconds(8);
            bool extended = service.ExtendReconnectWindow("player-1", disconnectedAt);

            // Không gia hạn thì token đã hết hạn ở giây thứ 15 (issuedAt+10); có gia hạn thì còn sống tới ~disconnectedAt+10=18s.
            var checkAt = issuedAt.AddSeconds(15);
            var result = service.ValidateSessionToken(issued.Token, checkAt);

            Assert.True(extended);
            Assert.Equal(SessionTokenValidationOutcome.Valid, result.Outcome);
        }

        [Fact]
        public void Revoke_InvalidatesTokenImmediately()
        {
            var service = CreateService();
            var now = DateTimeOffset.UtcNow;
            var issued = service.IssueToken("player-1", now);

            service.Revoke("player-1");

            var result = service.ValidateSessionToken(issued.Token, now);
            Assert.Equal(SessionTokenValidationOutcome.Invalid, result.Outcome);
        }

        [Fact]
        public void ValidateSessionToken_TokenFromDifferentPlayer_DoesNotCrossContaminate()
        {
            var service = CreateService();
            var now = DateTimeOffset.UtcNow;
            var tokenA = service.IssueToken("player-A", now);
            var tokenB = service.IssueToken("player-B", now);

            var resultA = service.ValidateSessionToken(tokenA.Token, now);
            var resultB = service.ValidateSessionToken(tokenB.Token, now);

            Assert.Equal("player-A", resultA.PlayerId);
            Assert.Equal("player-B", resultB.PlayerId);
        }
    }
}
