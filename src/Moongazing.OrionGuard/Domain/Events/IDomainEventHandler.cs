using System.Diagnostics.CodeAnalysis;

namespace Moongazing.OrionGuard.Domain.Events;

/// <summary>
/// Handles a domain event of type <typeparamref name="TEvent"/>. Resolved from the DI container by
/// <c>ServiceProviderDomainEventDispatcher</c>.
/// </summary>
/// <typeparam name="TEvent">The concrete domain event type this handler processes.</typeparam>
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix",
    Justification = "The 'EventHandler' suffix is the canonical DDD/.NET name for this concept (mirrors MediatR's INotificationHandler) and is required by the public API spec.")]
public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
{
    /// <summary>Handles the event. Implementations should be idempotent in production deployments.</summary>
    /// <param name="event">The event instance to handle.</param>
    /// <param name="cancellationToken">Token used to observe cancellation requests.</param>
    /// <returns>A task representing the asynchronous handling operation.</returns>
    [SuppressMessage("Naming", "CA1716:Identifiers should not match keywords",
        Justification = "'event' (escaped as @event) is idiomatic for this domain concept and required by the public API spec.")]
    Task HandleAsync(TEvent @event, CancellationToken cancellationToken);
}
