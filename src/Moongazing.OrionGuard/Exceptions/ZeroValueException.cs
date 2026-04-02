namespace Moongazing.OrionGuard.Exceptions;

/// <summary>
/// Exception thrown when a zero value is provided where it is not allowed.
/// </summary>
public sealed class ZeroValueException : GuardException
{
    public ZeroValueException(string parameterName)
        : base($"{parameterName} cannot be zero.", parameterName, "NOT_ZERO") { }
}
