namespace Moongazing.OrionGuard.Exceptions;

/// <summary>
/// Exception thrown when an invalid GUID is provided.
/// </summary>
public sealed class InvalidGuidException : GuardException
{
    public InvalidGuidException(string parameterName)
        : base($"{parameterName} is not a valid GUID.", parameterName, "INVALID_GUID") { }
}
