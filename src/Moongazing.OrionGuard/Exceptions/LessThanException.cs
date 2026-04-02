namespace Moongazing.OrionGuard.Exceptions;

/// <summary>
/// Exception thrown when a value is less than the required minimum.
/// </summary>
public sealed class LessThanException : GuardException
{
    public LessThanException(string parameterName)
        : base($"{parameterName} must be at least the minimum value.", parameterName, "LESS_THAN") { }

    public LessThanException(string parameterName, object minValue)
        : base($"{parameterName} must be at least {minValue}.", parameterName, "LESS_THAN") { }
}
