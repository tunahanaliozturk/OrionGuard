using System.Diagnostics.CodeAnalysis;
using Moongazing.OrionGuard.Domain.Events;

namespace Moongazing.OrionGuard.Testing.DomainEvents;

/// <summary>
/// <see cref="IDomainEventDispatcher"/> that records every dispatched event in memory instead of
/// invoking handlers. Intended for integration tests where you replace the production dispatcher
/// to assert that the right events left the application boundary.
/// </summary>
public sealed class InMemoryDomainEventDispatcher : IDomainEventDispatcher
{
    private readonly List<IDomainEvent> captured = new();

    /// <summary>Snapshot view of every event ever dispatched (in order).</summary>
    public IReadOnlyList<IDomainEvent> Captured => captured;

    /// <summary>Records the event in <see cref="Captured"/> without invoking any handlers.</summary>
    /// <param name="event">The event instance to capture.</param>
    /// <param name="cancellationToken">Token used to observe cancellation requests (unused; included to satisfy the interface).</param>
    /// <returns>A completed task.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="event"/> is <see langword="null"/>.</exception>
    [SuppressMessage("Naming", "CA1716:Identifiers should not match keywords",
        Justification = "Canonical DDD term aligned with public API contract from Task 3.")]
    public Task DispatchAsync(IDomainEvent @event, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);
        captured.Add(@event);
        return Task.CompletedTask;
    }

    /// <summary>Records all events in <see cref="Captured"/> in iteration order without invoking any handlers.</summary>
    /// <param name="events">The events to capture.</param>
    /// <param name="cancellationToken">Token used to observe cancellation requests (unused; included to satisfy the interface).</param>
    /// <returns>A completed task.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="events"/> is <see langword="null"/>.</exception>
    public Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(events);
        captured.AddRange(events);
        return Task.CompletedTask;
    }

    /// <summary>Entry point for fluent assertions over captured events.</summary>
    public DomainEventAssertions Should() => new(DomainEventCapture.FromList(captured));

    /// <summary>Removes all captured events.</summary>
    public void Clear() => captured.Clear();
}
