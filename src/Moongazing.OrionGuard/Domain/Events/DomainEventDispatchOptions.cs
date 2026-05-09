namespace Moongazing.OrionGuard.Domain.Events;

/// <summary>Configuration for <see cref="IDomainEventDispatcher"/>.</summary>
public sealed class DomainEventDispatchOptions
{
    /// <summary>How handlers are invoked for a single event. Default <see cref="DispatchMode.SequentialFailFast"/>.</summary>
    public DispatchMode Mode { get; init; } = DispatchMode.SequentialFailFast;
}
