namespace Moongazing.OrionGuard.Exceptions;

/// <summary>
/// Exception thrown when an invalid enum value is provided.
/// </summary>
public sealed class InvalidEnumValueException : GuardException
{
    public InvalidEnumValueException(string parameterName)
        : base($"{parameterName} is not a valid enum value.", parameterName, "INVALID_ENUM") { }
}
