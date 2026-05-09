using System.Diagnostics.CodeAnalysis;
using global::MediatR;
using Moongazing.OrionGuard.Domain.Events;

namespace Moongazing.OrionGuard.MediatR.DomainEvents;

/// <summary>
/// <see cref="IDomainEventDispatcher"/> that publishes events through MediatR's <see cref="IPublisher"/>.
/// Consumer events MUST also implement <see cref="INotification"/> — typically by adding it to
/// the event record declaration (e.g. <c>public sealed record X(Y Y) : DomainEventBase, INotification;</c>).
/// </summary>
public sealed class MediatRDomainEventDispatcher : IDomainEventDispatcher
{
    private readonly IPublisher publisher;

    /// <summary>Initializes a new instance of the <see cref="MediatRDomainEventDispatcher"/> class.</summary>
    /// <param name="publisher">The MediatR <see cref="IPublisher"/> used to publish notifications.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="publisher"/> is <see langword="null"/>.</exception>
    public MediatRDomainEventDispatcher(IPublisher publisher)
    {
        this.publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
    }

    /// <summary>Dispatches a single event by publishing it through MediatR.</summary>
    /// <param name="event">The event instance to dispatch. Must implement <see cref="INotification"/>.</param>
    /// <param name="cancellationToken">Token used to observe cancellation requests.</param>
    /// <returns>A task representing the asynchronous publish operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="event"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="event"/> does not implement <see cref="INotification"/>.</exception>
    [SuppressMessage("Naming", "CA1716:Identifiers should not match keywords",
        Justification = "Canonical DDD term aligned with public API contract from Task 3.")]
    public Task DispatchAsync(IDomainEvent @event, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);
        if (@event is not INotification notification)
        {
            throw new InvalidOperationException(
                $"{@event.GetType().FullName} must implement MediatR.INotification to use MediatRDomainEventDispatcher. " +
                $"Add ': INotification' to the event record's base list.");
        }
        return publisher.Publish(notification, cancellationToken);
    }

    /// <summary>Dispatches a batch of events sequentially in iteration order.</summary>
    /// <param name="events">The events to dispatch. Each must implement <see cref="INotification"/>.</param>
    /// <param name="cancellationToken">Token used to observe cancellation requests.</param>
    /// <returns>A task representing the asynchronous dispatch operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="events"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when any event does not implement <see cref="INotification"/>.</exception>
    public async Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(events);
        foreach (var e in events)
        {
            await DispatchAsync(e, cancellationToken).ConfigureAwait(false);
        }
    }
}
