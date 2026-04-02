namespace Moongazing.OrionGuard.Exceptions;

/// <summary>
/// Exception thrown when a value is true where false is required.
/// </summary>
public sealed class TrueException : GuardException
{
    public TrueException(string parameterName)
        : base($"{parameterName} cannot be true.", parameterName, "AGAINST_TRUE") { }
}
