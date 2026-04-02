namespace Moongazing.OrionGuard.Exceptions;

/// <summary>
/// Exception thrown when a negative integer value is provided.
/// </summary>
public sealed class NegativeException : GuardException
{
    public NegativeException(string parameterName)
        : base($"{parameterName} cannot be negative.", parameterName, "NOT_NEGATIVE") { }
}
