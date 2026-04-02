namespace Moongazing.OrionGuard.Exceptions;

/// <summary>
/// Exception thrown when a value exceeds the allowed maximum.
/// </summary>
public sealed class GreaterThanException : GuardException
{
    public GreaterThanException(string parameterName)
        : base($"{parameterName} must be at most the maximum value.", parameterName, "GREATER_THAN") { }

    public GreaterThanException(string parameterName, object maxValue)
        : base($"{parameterName} must be at most {maxValue}.", parameterName, "GREATER_THAN") { }
}
