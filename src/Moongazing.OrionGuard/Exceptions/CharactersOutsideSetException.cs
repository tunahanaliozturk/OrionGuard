namespace Moongazing.OrionGuard.Exceptions;

/// <summary>
/// Exception thrown when a string contains characters outside the allowed set.
/// </summary>
public sealed class CharactersOutsideSetException : GuardException
{
    public CharactersOutsideSetException(string parameterName)
        : base($"{parameterName} contains characters outside the set.", parameterName, "CHARACTERS_OUTSIDE_SET") { }
}
