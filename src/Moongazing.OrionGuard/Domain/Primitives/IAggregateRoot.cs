using System.Collections.Generic;
using Moongazing.OrionGuard.Domain.Events;

namespace Moongazing.OrionGuard.Domain.Primitives;

/// <summary>
/// Non-generic marker interface for aggregate roots. Enables consumers (e.g., the EF Core
/// interceptor in the MediatR package) to discover aggregates via <c>ChangeTracker.Entries</c>
/// without knowing the identifier type.
/// </summary>
public interface IAggregateRoot
{
    /// <summary>Domain events currently queued on this aggregate.</summary>
    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }

    /// <summary>Returns the queued events and clears the internal buffer atomically.</summary>
    IReadOnlyCollection<IDomainEvent> PullDomainEvents();
}
