namespace Moongazing.OrionGuard.Exceptions;

/// <summary>
/// Exception thrown when an invalid URL is provided.
/// </summary>
public sealed class InvalidUrlException : GuardException
{
    public InvalidUrlException(string parameterName)
        : base($"{parameterName} is not a valid URL.", parameterName, "INVALID_URL") { }
}
