using System;

namespace XiangqiOnline.Shared.Middleware;

/// <summary>P2-TV1-D3: cấu hình rate limit theo TỪNG connection/session (không phải toàn server).</summary>
public sealed class RateLimiterSettings
{
    /// <summary>Số message tối đa được xử lý trong 1 cửa sổ thời gian.</summary>
    public int MaxMessagesPerWindow { get; init; } = 20;

    /// <summary>Độ dài cửa sổ thời gian (ms).</summary>
    public int WindowMs { get; init; } = 1_000;

    /// <summary>
    /// Số lần vượt rate limit LIÊN TỤC trước khi coi là spam thật sự và đóng kết
    /// nối — cho phép vài lần vượt ngắn hạn (traffic dồn cục tự nhiên) mà không
    /// đóng kết nối oan, nhưng vẫn chặn spam kéo dài.
    /// </summary>
    public int MaxViolationsBeforeClose { get; init; } = 10;

    public void Validate()
    {
        if (MaxMessagesPerWindow <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaxMessagesPerWindow), "Phải > 0.");
        if (WindowMs <= 0)
            throw new ArgumentOutOfRangeException(nameof(WindowMs), "Phải > 0.");
        if (MaxViolationsBeforeClose <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaxViolationsBeforeClose), "Phải > 0.");
    }
}
