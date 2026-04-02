namespace Moongazing.OrionGuard.Exceptions;

/// <summary>
/// Exception thrown when a birth date is unrealistic (too far in the past or in the future).
/// </summary>
public sealed class UnrealisticBirthDateException : GuardException
{
    public UnrealisticBirthDateException(string parameterName)
        : base($"{parameterName} is not a realistic birth date.", parameterName, "UNREALISTIC_BIRTH_DATE") { }
}
