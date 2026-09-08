using System;

namespace XiangqiOnline.Shared.Transport;

/// <summary>
/// P2-TV1-D1: cấu hình heartbeat + timeout, tách rõ 2 tầng theo yêu cầu kế hoạch:
///
/// - TransportReadTimeoutMs: timeout ở TẦNG TRANSPORT — "không có byte nào tới,
///   socket có thể đã chết" (mất mạng đột ngột, cáp rút, NAT timeout im lặng).
///   Đây là bảo vệ tối thiểu, không quan tâm ứng dụng đang làm gì.
///
/// - HeartbeatIntervalMs / HeartbeatTimeoutMs: timeout ở TẦNG NGHIỆP VỤ —
///   "kết nối còn sống về mặt TCP, nhưng phía kia có thực sự còn phản hồi
///   không". Độc lập với transport timeout: một kết nối có thể transport OK
///   (socket vẫn mở) nhưng nghiệp vụ đã treo (peer bị deadlock, thread xử lý
///   game logic bị đơ) — heartbeat bắt được ca này, transport timeout thì không.
/// </summary>
public sealed class HeartbeatSettings
{
    /// <summary>Không có traffic gì (kể cả PING/PONG) trong khoảng này -> gửi PING.</summary>
    public int HeartbeatIntervalMs { get; init; } = 5_000;

    /// <summary>Không có traffic gì trong khoảng này -> coi là chết, báo TimedOut.</summary>
    public int HeartbeatTimeoutMs { get; init; } = 15_000;

    /// <summary>Timeout tầng transport khi chờ đọc 1 frame trọn vẹn từ socket.</summary>
    public int TransportReadTimeoutMs { get; init; } = 20_000;

    /// <summary>Chu kỳ polling nội bộ của HeartbeatMonitor. Chỉ chỉnh nhỏ khi viết test.</summary>
    public int PollIntervalMs { get; init; } = 1_000;

    public void Validate()
    {
        if (HeartbeatIntervalMs <= 0)
            throw new ArgumentOutOfRangeException(nameof(HeartbeatIntervalMs), "Phải > 0.");
        if (HeartbeatTimeoutMs <= HeartbeatIntervalMs)
            throw new ArgumentOutOfRangeException(nameof(HeartbeatTimeoutMs),
                "HeartbeatTimeoutMs phải lớn hơn HeartbeatIntervalMs, nếu không sẽ báo chết ngay cả khi đang đúng chu kỳ gửi PING.");
        if (TransportReadTimeoutMs <= HeartbeatTimeoutMs)
            throw new ArgumentOutOfRangeException(nameof(TransportReadTimeoutMs),
                "TransportReadTimeoutMs nên lớn hơn HeartbeatTimeoutMs — heartbeat (tầng nghiệp vụ) phải là cơ chế phát hiện chính, transport timeout chỉ là lưới an toàn cuối cùng.");
        if (PollIntervalMs <= 0)
            throw new ArgumentOutOfRangeException(nameof(PollIntervalMs), "Phải > 0.");
    }
}
