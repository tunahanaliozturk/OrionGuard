namespace Moongazing.OrionGuard.Exceptions;

/// <summary>
/// Exception thrown when a file is empty.
/// </summary>
public sealed class EmptyFileException : GuardException
{
    public EmptyFileException(string parameterName)
        : base($"{parameterName} cannot be empty.", parameterName, "EMPTY_FILE") { }
}
