using Moongazing.OrionGuard.Exceptions;
using Moongazing.OrionGuard.Extensions;

namespace Moongazing.OrionGuard.Tests;

public class RateLimitGuardsTests
{
    #region AgainstRateLimitExceeded

    [Theory]
    [InlineData(0, 100)]
    [InlineData(50, 100)]
    [InlineData(99, 100)]
    public void AgainstRateLimitExceeded_ShouldNotThrow_WhenUnderLimit(int current, int max)
    {
        var exception = Record.Exception(() => current.AgainstRateLimitExceeded(max, "requestCount"));
        Assert.Null(exception);
    }

    [Theory]
    [InlineData(100, 100)]
    [InlineData(150, 100)]
    public void AgainstRateLimitExceeded_ShouldThrow_WhenAtOrOverLimit(int current, int max)
    {
        Assert.Throws<RateLimitExceededException>(() =>
            current.AgainstRateLimitExceeded(max, "requestCount"));
    }

    [Fact]
    public void AgainstRateLimitExceeded_ShouldThrow_WhenExactlyAtLimit()
    {
        Assert.Throws<RateLimitExceededException>(() =>
            10.AgainstRateLimitExceeded(10, "requestCount"));
    }

    #endregion

    #region AgainstTooManyRequests

    [Fact]
    public void AgainstTooManyRequests_ShouldNotThrow_WhenIntervalRespected()
    {
        var elapsed = TimeSpan.FromSeconds(5);
        var minimum = TimeSpan.FromSeconds(1);

        var exception = Record.Exception(() => elapsed.AgainstTooManyRequests(minimum, "interval"));
        Assert.Null(exception);
    }

    [Fact]
    public void AgainstTooManyRequests_ShouldNotThrow_WhenExactlyAtInterval()
    {
        var interval = TimeSpan.FromSeconds(2);

        var exception = Record.Exception(() => interval.AgainstTooManyRequests(interval, "interval"));
        Assert.Null(exception);
    }

    [Fact]
    public void AgainstTooManyRequests_ShouldThrow_WhenTooSoon()
    {
        var elapsed = TimeSpan.FromMilliseconds(500);
        var minimum = TimeSpan.FromSeconds(1);

        Assert.Throws<RateLimitExceededException>(() =>
            elapsed.AgainstTooManyRequests(minimum, "interval"));
    }

    #endregion

    #region AgainstSlidingWindowExceeded

    [Theory]
    [InlineData(0, 100)]
    [InlineData(50, 100)]
    [InlineData(99, 100)]
    public void AgainstSlidingWindowExceeded_ShouldNotThrow_WhenUnderLimit(int requestCount, int max)
    {
        var window = TimeSpan.FromMinutes(1);

        var exception = Record.Exception(() =>
            requestCount.AgainstSlidingWindowExceeded(max, window, "requests"));
        Assert.Null(exception);
    }

    [Theory]
    [InlineData(100, 100)]
    [InlineData(200, 100)]
    public void AgainstSlidingWindowExceeded_ShouldThrow_WhenAtOrOverLimit(int requestCount, int max)
    {
        var window = TimeSpan.FromMinutes(1);

        Assert.Throws<RateLimitExceededException>(() =>
            requestCount.AgainstSlidingWindowExceeded(max, window, "requests"));
    }

    #endregion

    #region AgainstConcurrentLimitExceeded

    [Theory]
    [InlineData(0, 10)]
    [InlineData(5, 10)]
    [InlineData(9, 10)]
    public void AgainstConcurrentLimitExceeded_ShouldNotThrow_WhenUnderLimit(int active, int max)
    {
        var exception = Record.Exception(() =>
            active.AgainstConcurrentLimitExceeded(max, "connections"));
        Assert.Null(exception);
    }

    [Theory]
    [InlineData(10, 10)]
    [InlineData(15, 10)]
    public void AgainstConcurrentLimitExceeded_ShouldThrow_WhenAtOrOverLimit(int active, int max)
    {
        Assert.Throws<RateLimitExceededException>(() =>
            active.AgainstConcurrentLimitExceeded(max, "connections"));
    }

    #endregion

    #region AgainstDailyQuotaExceeded

    [Theory]
    [InlineData(0L, 1000L)]
    [InlineData(500L, 1000L)]
    [InlineData(999L, 1000L)]
    public void AgainstDailyQuotaExceeded_ShouldNotThrow_WhenUnderQuota(long current, long limit)
    {
        var exception = Record.Exception(() =>
            current.AgainstDailyQuotaExceeded(limit, "usage"));
        Assert.Null(exception);
    }

    [Theory]
    [InlineData(1000L, 1000L)]
    [InlineData(2000L, 1000L)]
    public void AgainstDailyQuotaExceeded_ShouldThrow_WhenAtOrOverQuota(long current, long limit)
    {
        Assert.Throws<RateLimitExceededException>(() =>
            current.AgainstDailyQuotaExceeded(limit, "usage"));
    }

    #endregion
}
