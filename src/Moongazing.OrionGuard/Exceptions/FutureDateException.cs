namespace Moongazing.OrionGuard.Exceptions;

/// <summary>
/// Exception thrown when a date is in the future.
/// </summary>
public sealed class FutureDateException : GuardException
{
    public FutureDateException(string parameterName)
        : base($"{parameterName} cannot be in the future.", parameterName, "FUTURE_DATE") { }
}
