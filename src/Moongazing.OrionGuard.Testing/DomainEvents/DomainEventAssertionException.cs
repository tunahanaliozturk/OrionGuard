namespace Moongazing.OrionGuard.Testing.DomainEvents;

/// <summary>
/// Thrown by <see cref="DomainEventAssertions"/> when an expectation about captured domain events fails.
/// Test runners (xUnit, NUnit, MSTest) treat any thrown exception as a test failure, so this works
/// without depending on a specific framework's assertion type.
/// </summary>
public sealed class DomainEventAssertionException : Exception
{
    /// <summary>Initializes a new instance with the supplied message.</summary>
    public DomainEventAssertionException(string message) : base(message) { }
}
