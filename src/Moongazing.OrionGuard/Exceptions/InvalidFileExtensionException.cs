namespace Moongazing.OrionGuard.Exceptions;

/// <summary>
/// Exception thrown when a file has an unsupported extension.
/// </summary>
public sealed class InvalidFileExtensionException : GuardException
{
    public InvalidFileExtensionException(string parameterName)
        : base($"{parameterName} is not a valid file extension.", parameterName, "INVALID_FILE_EXTENSION") { }
}
