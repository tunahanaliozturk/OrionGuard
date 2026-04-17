using System;

namespace Moongazing.OrionGuard.Domain.Exceptions;

/// <summary>
/// Thrown when a domain invariant (as opposed to a named business rule) is violated, for example
/// when an aggregate receives an inconsistent combination of arguments.
/// </summary>
public sealed class DomainInvariantException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DomainInvariantException"/> class with a message.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    public DomainInvariantException(string message) : base(message) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="DomainInvariantException"/> class with a message and an inner exception.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    /// <param name="inner">The exception that is the cause of the current exception.</param>
    public DomainInvariantException(string message, Exception inner) : base(message, inner) { }
}
