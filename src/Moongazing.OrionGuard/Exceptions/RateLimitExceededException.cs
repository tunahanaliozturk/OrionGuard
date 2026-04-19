namespace Moongazing.OrionGuard.Exceptions;

/// <summary>
/// Exception thrown when a rate limit or quota has been exceeded.
/// </summary>
public sealed class RateLimitExceededException : GuardException
{
    public RateLimitExceededException(string parameterName, string detail)
        : base($"{parameterName} rate limit exceeded: {detail}.", parameterName, "RATE_LIMIT_EXCEEDED") { }
}
