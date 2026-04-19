namespace Moongazing.OrionGuard.Core;

/// <summary>
/// Factory interface for creating custom validation exceptions.
/// Register via DI to override default exception creation behavior.
/// </summary>
public interface IExceptionFactory
{
    /// <summary>
    /// Creates an exception for a validation failure.
    /// </summary>
    /// <param name="errorCode">The error code (e.g., "NOT_NULL", "INVALID_EMAIL")</param>
    /// <param name="parameterName">The parameter that failed validation</param>
    /// <param name="message">The error message</param>
    /// <param name="innerException">Optional inner exception</param>
    /// <returns>The exception to throw</returns>
    Exception CreateException(string errorCode, string parameterName, string message, Exception? innerException = null);
}
