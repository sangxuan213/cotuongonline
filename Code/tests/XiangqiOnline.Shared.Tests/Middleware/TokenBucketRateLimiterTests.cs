using System.Threading.Tasks;
using XiangqiOnline.Shared.Middleware;
using Xunit;

namespace XiangqiOnline.Shared.Tests.Middleware;

public class TokenBucketRateLimiterTests
{
    [Fact]
    public void TryConsume_WithinCapacity_AlwaysSucceeds()
    {
        var limiter = new TokenBucketRateLimiter(new RateLimiterSettings { MaxMessagesPerWindow = 5, WindowMs = 1000 });

        for (int i = 0; i < 5; i++)
            Assert.True(limiter.TryConsume());
    }

    [Fact]
    public void TryConsume_BeyondCapacity_Fails()
    {
        var limiter = new TokenBucketRateLimiter(new RateLimiterSettings { MaxMessagesPerWindow = 3, WindowMs = 1000 });

        for (int i = 0; i < 3; i++)
            Assert.True(limiter.TryConsume());

        Assert.False(limiter.TryConsume()); // vượt hạn mức
    }

    [Fact]
    public async Task TryConsume_AfterWindowElapses_RefillsAndAllowsMore()
    {
        var limiter = new TokenBucketRateLimiter(new RateLimiterSettings { MaxMessagesPerWindow = 2, WindowMs = 100 });

        Assert.True(limiter.TryConsume());
        Assert.True(limiter.TryConsume());
        Assert.False(limiter.TryConsume());

        await Task.Delay(150); // > WindowMs -> đủ nạp lại token

        Assert.True(limiter.TryConsume());
    }

    [Fact]
    public void TryConsume_DifferentInstances_AreIndependent()
    {
        // Đúng tinh thần "rate limit theo connection" — 2 connection khác nhau
        // (2 instance khác nhau) không ảnh hưởng lẫn nhau.
        var limiterA = new TokenBucketRateLimiter(new RateLimiterSettings { MaxMessagesPerWindow = 1, WindowMs = 10_000 });
        var limiterB = new TokenBucketRateLimiter(new RateLimiterSettings { MaxMessagesPerWindow = 1, WindowMs = 10_000 });

        Assert.True(limiterA.TryConsume());
        Assert.False(limiterA.TryConsume()); // A đã hết hạn mức

        Assert.True(limiterB.TryConsume()); // B không bị ảnh hưởng bởi A
    }
}
