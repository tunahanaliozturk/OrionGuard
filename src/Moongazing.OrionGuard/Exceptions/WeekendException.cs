namespace Moongazing.OrionGuard.Exceptions;

/// <summary>
/// Exception thrown when a date falls on a weekend.
/// </summary>
public sealed class WeekendException : GuardException
{
    public WeekendException(string parameterName)
        : base($"{parameterName} cannot be a weekend.", parameterName, "WEEKEND") { }
}
