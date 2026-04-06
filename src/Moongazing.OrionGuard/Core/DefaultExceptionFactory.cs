namespace Moongazing.OrionGuard.Core;

/// <summary>
/// Default exception factory that creates standard ArgumentException/ArgumentNullException instances.
/// </summary>
public sealed class DefaultExceptionFactory : IExceptionFactory
{
    public static readonly DefaultExceptionFactory Instance = new();

    public Exception CreateException(string errorCode, string parameterName, string message, Exception? innerException = null)
    {
        return errorCode.ToUpperInvariant() switch
        {
            "NOT_NULL" => new ArgumentNullException(parameterName, message),
            "OUT_OF_RANGE" or "GREATER_THAN" or "LESS_THAN" => new ArgumentOutOfRangeException(parameterName, message),
            _ => new ArgumentException(message, parameterName)
        };
    }
}
