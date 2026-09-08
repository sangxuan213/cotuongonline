using System.Threading.Tasks;
using XiangqiOnline.Shared.Middleware;
using Xunit;

namespace XiangqiOnline.Shared.Tests.Middleware;

public class BoundedIdempotencyCacheTests
{
    [Fact]
    public void Set_ThenTryGet_ReturnsStoredResult()
    {
        var cache = new BoundedIdempotencyCache<string>(new IdempotencyCacheSettings { MaxEntries = 10, EntryTtlMs = 5000 });

        cache.Set("req-1", "ket-qua-1");
        bool found = cache.TryGet("req-1", out var result);

        Assert.True(found);
        Assert.Equal("ket-qua-1", result);
    }

    [Fact]
    public void TryGet_UnknownRequestId_ReturnsFalse()
    {
        var cache = new BoundedIdempotencyCache<string>(new IdempotencyCacheSettings { MaxEntries = 10, EntryTtlMs = 5000 });

        bool found = cache.TryGet("khong-ton-tai", out _);

        Assert.False(found);
    }

    [Fact]
    public void Set_BeyondMaxEntries_EvictsOldestAndNeverGrowsUnbounded()
    {
        // Đúng tiêu chí nghiệm thu: "cache không tăng vô hạn" — nhồi gấp 3 lần
        // MaxEntries, Count phải luôn <= MaxEntries.
        var cache = new BoundedIdempotencyCache<int>(new IdempotencyCacheSettings { MaxEntries = 5, EntryTtlMs = 60_000 });

        for (int i = 0; i < 15; i++)
        {
            cache.Set($"req-{i}", i);
            Assert.True(cache.Count <= 5);
        }

        Assert.Equal(5, cache.Count);

        // Entry cũ nhất (req-0..req-9) phải đã bị loại — chỉ còn 5 cái mới nhất (req-10..req-14).
        Assert.False(cache.TryGet("req-0", out _));
        Assert.True(cache.TryGet("req-14", out var last));
        Assert.Equal(14, last);
    }

    [Fact]
    public void TryGet_RefreshesLruOrder_RecentlyAccessedSurvivesEviction()
    {
        var cache = new BoundedIdempotencyCache<int>(new IdempotencyCacheSettings { MaxEntries = 3, EntryTtlMs = 60_000 });

        cache.Set("req-A", 1);
        cache.Set("req-B", 2);
        cache.Set("req-C", 3);

        // Truy cập lại req-A -> đưa lên "mới dùng gần đây nhất", req-B mới là cũ nhất.
        cache.TryGet("req-A", out _);

        cache.Set("req-D", 4); // đầy 4/3 -> phải loại req-B (cũ nhất, chưa được refresh)

        Assert.True(cache.TryGet("req-A", out _)); // sống sót nhờ vừa được truy cập
        Assert.False(cache.TryGet("req-B", out _)); // bị loại
    }

    [Fact]
    public async Task TryGet_AfterTtlExpires_ReturnsFalse()
    {
        var cache = new BoundedIdempotencyCache<string>(new IdempotencyCacheSettings { MaxEntries = 10, EntryTtlMs = 50 });

        cache.Set("req-1", "ket-qua-cu");
        await Task.Delay(120); // > TTL

        bool found = cache.TryGet("req-1", out _);

        Assert.False(found);
    }

    [Fact]
    public void Set_SameRequestIdTwice_OverwritesAndDoesNotDoubleCount()
    {
        var cache = new BoundedIdempotencyCache<string>(new IdempotencyCacheSettings { MaxEntries = 10, EntryTtlMs = 5000 });

        cache.Set("req-1", "ket-qua-cu");
        cache.Set("req-1", "ket-qua-moi");

        Assert.Equal(1, cache.Count);
        cache.TryGet("req-1", out var result);
        Assert.Equal("ket-qua-moi", result);
    }
}
