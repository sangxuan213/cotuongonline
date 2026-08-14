using System;

namespace XiangqiOnline.Shared.Middleware
{
    /// <summary>P2-TV1-D2: cấu hình giới hạn cho BoundedIdempotencyCache.</summary>
    public sealed class IdempotencyCacheSettings
    {
        /// <summary>Số requestId tối đa được nhớ cùng lúc — vượt quá thì loại bỏ entry cũ nhất (LRU).</summary>
        public int MaxEntries { get; init; } = 2_000;

        /// <summary>Kết quả cache "ngắn hạn" theo đúng §10.6 — sau khoảng này, retry sẽ chạy lại như request mới.</summary>
        public int EntryTtlMs { get; init; } = 60_000;

        public void Validate()
        {
            if (MaxEntries <= 0)
                throw new ArgumentOutOfRangeException(nameof(MaxEntries), "Phải > 0.");
            if (EntryTtlMs <= 0)
                throw new ArgumentOutOfRangeException(nameof(EntryTtlMs), "Phải > 0.");
        }
    }
}
