namespace Moongazing.OrionGuard.Exceptions;

/// <summary>
/// Base exception for all guard-related validation errors.
/// Provides structured error information including error codes and parameter names.
/// </summary>
public class GuardException : Exception
{
    /// <summary>
    /// The name of the parameter that failed validation.
    /// </summary>
    public string? ParameterName { get; }

    /// <summary>
    /// A machine-readable error code for programmatic error handling.
    /// </summary>
    public string? ErrorCode { get; }

    public GuardException(string message)
        : base(message)
    {
    }

    public GuardException(string message, string parameterName, string? errorCode = null)
        : base(message)
    {
        ParameterName = parameterName;
        ErrorCode = errorCode;
    }

    public GuardException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public GuardException(string message, string parameterName, string? errorCode, Exception innerException)
        : base(message, innerException)
    {
        ParameterName = parameterName;
        ErrorCode = errorCode;
    }
}
