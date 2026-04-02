namespace Moongazing.OrionGuard.Exceptions;

/// <summary>
/// Exception thrown when a value is false where true is required.
/// </summary>
public sealed class FalseException : GuardException
{
    public FalseException(string parameterName)
        : base($"{parameterName} cannot be false.", parameterName, "AGAINST_FALSE") { }
}
