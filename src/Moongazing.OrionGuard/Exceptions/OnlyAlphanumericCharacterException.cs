namespace Moongazing.OrionGuard.Exceptions;

/// <summary>
/// Exception thrown when a string does not contain only alphanumeric characters.
/// </summary>
public sealed class OnlyAlphanumericCharacterException : GuardException
{
    public OnlyAlphanumericCharacterException(string parameterName)
        : base($"{parameterName} must contain only alphanumeric characters.", parameterName, "ALPHANUMERIC_ONLY") { }
}
