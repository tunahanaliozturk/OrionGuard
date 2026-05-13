using System.Diagnostics.CodeAnalysis;

namespace Moongazing.OrionGuard.Domain.Events;

/// <summary>
/// Dispatches domain events to their registered handlers. The default implementation
/// (<c>ServiceProviderDomainEventDispatcher</c>) resolves <see cref="IDomainEventHandler{TEvent}"/>
/// instances from <see cref="IServiceProvider"/>; the MediatR bridge instead delegates to <c>IPublisher</c>.
/// </summary>
public interface IDomainEventDispatcher
{
    /// <summary>Dispatches a single event.</summary>
    /// <param name="event">The event instance to dispatch.</param>
    /// <param name="cancellationToken">Token used to observe cancellation requests.</param>
    /// <returns>A task representing the asynchronous dispatch operation.</returns>
    [SuppressMessage("Naming", "CA1716:Identifiers should not match keywords",
        Justification = "'event' (escaped as @event) is idiomatic for this domain concept and required by the public API spec.")]
    Task DispatchAsync(IDomainEvent @event, CancellationToken cancellationToken = default);

    /// <summary>Dispatches a batch of events. Default implementations process them in iteration order.</summary>
    /// <param name="events">The events to dispatch.</param>
    /// <param name="cancellationToken">Token used to observe cancellation requests.</param>
    /// <returns>A task representing the asynchronous dispatch operation.</returns>
    Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken cancellationToken = default);
}
