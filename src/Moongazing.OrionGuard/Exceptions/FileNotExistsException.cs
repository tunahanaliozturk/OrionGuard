namespace Moongazing.OrionGuard.Exceptions;

/// <summary>
/// Exception thrown when a referenced file does not exist.
/// </summary>
public sealed class FileNotExistsException : GuardException
{
    public FileNotExistsException(string parameterName)
        : base($"{parameterName} does not exist.", parameterName, "FILE_NOT_EXISTS") { }
}
