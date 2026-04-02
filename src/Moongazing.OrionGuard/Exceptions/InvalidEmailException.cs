namespace Moongazing.OrionGuard.Exceptions;

/// <summary>
/// Exception thrown when an invalid email is provided.
/// </summary>
public sealed class InvalidEmailException : GuardException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InvalidEmailException"/> class.
    /// </summary>
    /// <param name="parameterName">The parameter name for the invalid email.</param>
    public InvalidEmailException(string parameterName)
        : base($"{parameterName} is not a valid email address.", parameterName, "INVALID_EMAIL") { }
}
