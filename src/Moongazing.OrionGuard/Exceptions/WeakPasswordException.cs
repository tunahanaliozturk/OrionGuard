namespace Moongazing.OrionGuard.Exceptions;

/// <summary>
/// Exception thrown when a password does not meet strength requirements.
/// </summary>
public sealed class WeakPasswordException : GuardException
{
    public WeakPasswordException(string parameterName)
        : base($"{parameterName} is too weak.", parameterName, "WEAK_PASSWORD") { }
}
