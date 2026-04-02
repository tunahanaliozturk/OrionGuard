namespace Moongazing.OrionGuard.Exceptions;

/// <summary>
/// Exception thrown when a date is in the past where a future date is required.
/// </summary>
public sealed class PastDateException : GuardException
{
    public PastDateException(string parameterName)
        : base($"{parameterName} cannot be in the past.", parameterName, "PAST_DATE") { }
}
