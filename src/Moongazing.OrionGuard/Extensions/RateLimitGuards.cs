using Moongazing.OrionGuard.Exceptions;

namespace Moongazing.OrionGuard.Extensions;

/// <summary>
/// Stateless rate limit and quota validation guards.
/// These methods perform threshold checks against provided values
/// without maintaining any internal state or counters.
/// </summary>
public static class RateLimitGuards
{
    /// <summary>
    /// Validates that a request count has not exceeded the allowed limit.
    /// </summary>
    /// <param name="currentCount">The current number of requests made.</param>
    /// <param name="maxAllowed">The maximum number of requests allowed.</param>
    /// <param name="parameterName">The name of the parameter being validated.</param>
    /// <exception cref="RateLimitExceededException">
    /// Thrown when <paramref name="currentCount"/> is greater than or equal to <paramref name="maxAllowed"/>.
    /// </exception>
    public static void AgainstRateLimitExceeded(this int currentCount, int maxAllowed, string parameterName)
    {
        if (currentCount >= maxAllowed)
        {
            throw new RateLimitExceededException(parameterName,
                $"request count {currentCount} has reached the maximum of {maxAllowed}");
        }
    }

    /// <summary>
    /// Validates that the minimum interval between requests has been respected.
    /// </summary>
    /// <param name="timeSinceLastRequest">The elapsed time since the last request.</param>
    /// <param name="minimumInterval">The minimum required interval between requests.</param>
    /// <param name="parameterName">The name of the parameter being validated.</param>
    /// <exception cref="RateLimitExceededException">
    /// Thrown when <paramref name="timeSinceLastRequest"/> is less than <paramref name="minimumInterval"/>.
    /// </exception>
    public static void AgainstTooManyRequests(this TimeSpan timeSinceLastRequest, TimeSpan minimumInterval, string parameterName)
    {
        if (timeSinceLastRequest < minimumInterval)
        {
            throw new RateLimitExceededException(parameterName,
                $"only {timeSinceLastRequest.TotalMilliseconds:F0}ms elapsed since last request, minimum interval is {minimumInterval.TotalMilliseconds:F0}ms");
        }
    }

    /// <summary>
    /// Validates that the request count has not exceeded the limit within a sliding time window.
    /// The caller is responsible for tracking the request count and window boundaries.
    /// </summary>
    /// <param name="requestCount">The number of requests within the current window.</param>
    /// <param name="maxRequests">The maximum number of requests allowed within the window.</param>
    /// <param name="window">The duration of the sliding time window.</param>
    /// <param name="parameterName">The name of the parameter being validated.</param>
    /// <exception cref="RateLimitExceededException">
    /// Thrown when <paramref name="requestCount"/> is greater than or equal to <paramref name="maxRequests"/>.
    /// </exception>
    public static void AgainstSlidingWindowExceeded(this int requestCount, int maxRequests, TimeSpan window, string parameterName)
    {
        if (requestCount >= maxRequests)
        {
            throw new RateLimitExceededException(parameterName,
                $"{requestCount} requests in a {window.TotalSeconds:F0}s window exceeds the limit of {maxRequests}");
        }
    }

    /// <summary>
    /// Validates that the number of active connections has not exceeded the concurrent limit.
    /// </summary>
    /// <param name="activeConnections">The current number of active connections.</param>
    /// <param name="maxConnections">The maximum number of concurrent connections allowed.</param>
    /// <param name="parameterName">The name of the parameter being validated.</param>
    /// <exception cref="RateLimitExceededException">
    /// Thrown when <paramref name="activeConnections"/> is greater than or equal to <paramref name="maxConnections"/>.
    /// </exception>
    public static void AgainstConcurrentLimitExceeded(this int activeConnections, int maxConnections, string parameterName)
    {
        if (activeConnections >= maxConnections)
        {
            throw new RateLimitExceededException(parameterName,
                $"{activeConnections} active connections has reached the maximum of {maxConnections}");
        }
    }

    /// <summary>
    /// Validates that the current usage has not exceeded the daily quota.
    /// </summary>
    /// <param name="currentUsage">The current usage count for the day.</param>
    /// <param name="dailyLimit">The maximum allowed daily usage.</param>
    /// <param name="parameterName">The name of the parameter being validated.</param>
    /// <exception cref="RateLimitExceededException">
    /// Thrown when <paramref name="currentUsage"/> is greater than or equal to <paramref name="dailyLimit"/>.
    /// </exception>
    public static void AgainstDailyQuotaExceeded(this long currentUsage, long dailyLimit, string parameterName)
    {
        if (currentUsage >= dailyLimit)
        {
            throw new RateLimitExceededException(parameterName,
                $"daily usage {currentUsage} has reached the quota of {dailyLimit}");
        }
    }
}
