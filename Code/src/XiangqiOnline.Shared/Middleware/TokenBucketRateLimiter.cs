using System;
using System.Threading;

namespace XiangqiOnline.Shared.Middleware;

/// <summary>
/// P2-TV1-D3: token-bucket rate limiter DÙNG RIÊNG CHO 1 CONNECTION — mỗi kết nối
/// tự có 1 instance, không chia sẻ giữa các connection (đúng yêu cầu "rate limit
/// theo connection/session", không phải giới hạn toàn server).
///
/// Nạp lại token liên tục theo thời gian trôi qua (không phải "reset cả cửa sổ" theo
/// mốc cố định) — tránh hiện tượng dồn cục ở đúng ranh giới cửa sổ (vd. window 1s,
/// client gửi 20 msg cuối giây này + 20 msg đầu giây sau = 40 msg trong tích tắc vẫn
/// lọt qua nếu dùng "đếm theo mốc cố định").
/// </summary>
public sealed class TokenBucketRateLimiter
{
    private readonly double _capacity;
    private readonly double _refillPerMs;
    private double _tokens;
    private long _lastRefillTicks;
    private readonly Lock _lock = new();

    public TokenBucketRateLimiter(RateLimiterSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        _capacity = settings.MaxMessagesPerWindow;
        _refillPerMs = (double)settings.MaxMessagesPerWindow / settings.WindowMs;
        _tokens = _capacity; // bắt đầu đầy — cho phép burst hợp lệ ngay từ đầu (vd. đồng bộ trạng thái lúc mới kết nối)
        _lastRefillTicks = Environment.TickCount64;
    }

    /// <summary>Trả về true nếu còn hạn mức (tiêu 1 token); false nếu đã vượt rate limit.</summary>
    public bool TryConsume()
    {
        lock (_lock)
        {
            Refill();
            if (_tokens < 1.0) return false;
            _tokens -= 1.0;
            return true;
        }
    }

    private void Refill()
    {
        long now = Environment.TickCount64;
        long elapsedMs = now - _lastRefillTicks;
        if (elapsedMs <= 0) return;

        _tokens = Math.Min(_capacity, _tokens + elapsedMs * _refillPerMs);
        _lastRefillTicks = now;
    }
}
