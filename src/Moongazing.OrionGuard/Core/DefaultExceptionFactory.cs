namespace Moongazing.OrionGuard.Core;

/// <summary>
/// Exception factory that creates standard ArgumentException/ArgumentNullException instances.
/// </summary>
/// <remarks>
/// This is not what OrionGuard's guards throw: they throw their own types (for example
/// <c>NullValueException</c> for a null value, not <see cref="ArgumentNullException"/>) and never call an
/// <see cref="IExceptionFactory"/>.
/// </remarks>
[Obsolete("OrionGuard guards never call IExceptionFactory, and this factory's exception types differ from the ones guards throw. " +
          "It will be removed in v7.")]
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
