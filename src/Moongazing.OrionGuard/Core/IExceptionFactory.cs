namespace Moongazing.OrionGuard.Core;

/// <summary>
/// Factory interface for creating validation exceptions from an error code, parameter name and message.
/// </summary>
/// <remarks>
/// <para>
/// OrionGuard's own guards do not call this interface. <c>Guard</c>, <c>Ensure</c>, <c>FastGuard</c> and the
/// extension guards always throw their own exception types (<c>NullValueException</c>,
/// <c>GuardException</c>, <see cref="ArgumentException"/>, ...), whatever factory is registered.
/// </para>
/// <para>
/// This interface, its registration entry points (<c>AddOrionGuardExceptionFactory</c>,
/// <see cref="ExceptionFactoryProvider"/>) and <see cref="DefaultExceptionFactory"/> are obsolete and will be
/// removed in v7. To map guard failures to your own exception types, catch <c>GuardException</c> (or the
/// specific type) at your boundary.
/// </para>
/// </remarks>
[Obsolete("OrionGuard guards never call IExceptionFactory, so implementing or registering one does not change the exceptions they throw. " +
          "Catch GuardException (or the specific exception type) and translate it at your boundary instead. This interface will be removed in v7.")]
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
