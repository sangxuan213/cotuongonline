using System;

namespace XiangqiOnline.Shared.Session;

/// <summary>P3-TV1-D1: cấu hình theo đúng §15.2 (Security baseline) + §9.3 (Reconnect profile).</summary>
public sealed class SessionTokenSettings
{
    /// <summary>Số byte ngẫu nhiên sinh token — 32 byte = 256 bit, đúng tối thiểu §15.2.</summary>
    public int TokenSizeBytes { get; init; } = 32;

    /// <summary>Cửa sổ reconnect — §9.3: "60 giây, cấu hình được".</summary>
    public int ReconnectWindowSeconds { get; init; } = 60;

    public void Validate()
    {
        if (TokenSizeBytes < 32)
            throw new ArgumentOutOfRangeException(nameof(TokenSizeBytes), "Phải >= 32 byte (256 bit) theo §15.2.");
        if (ReconnectWindowSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(ReconnectWindowSeconds), "Phải > 0.");
    }
}
