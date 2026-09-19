namespace Moongazing.OrionGuard.Testing.Validators;

/// <summary>
/// Thrown when a validator snapshot does not match, or when rule coverage is incomplete.
/// Test runners (xUnit, NUnit, MSTest) treat any thrown exception as a test failure, so this works
/// without depending on a specific framework's assertion type.
/// </summary>
public sealed class ValidatorAssertionException : Exception
{
    /// <summary>Initializes a new instance with the supplied message.</summary>
    public ValidatorAssertionException(string message) : base(message) { }
}
