namespace Moongazing.OrionGuard.Exceptions;

/// <summary>
/// Exception thrown when a date falls on a specific disallowed day of the week.
/// </summary>
public sealed class SpecifyDayException : GuardException
{
    public SpecifyDayException(string parameterName, DayOfWeek day)
        : base($"{parameterName} cannot be {day}.", parameterName, "SPECIFIC_DAY") { }
}
