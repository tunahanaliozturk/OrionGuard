namespace Moongazing.OrionGuard.Exceptions;

/// <summary>
/// Exception thrown when a collection exceeds the maximum allowed count.
/// </summary>
public sealed class ExceedingCountException : GuardException
{
    public ExceedingCountException(string parameterName, int count)
       : base($"{parameterName} cannot exceed {count}.", parameterName, "EXCEEDING_COUNT") { }
}
