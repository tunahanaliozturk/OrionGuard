namespace Moongazing.OrionGuard.Domain.Events;

/// <summary>Strategy used by <see cref="IDomainEventDispatcher"/> to invoke handlers for a single event.</summary>
public enum DispatchMode
{
    /// <summary>Run handlers in registration order; first exception aborts and propagates.</summary>
    SequentialFailFast,

    /// <summary>Run handlers in registration order; collect exceptions and rethrow as <see cref="AggregateException"/> at the end.</summary>
    SequentialContinueOnError,

    /// <summary>Run handlers concurrently via <see cref="Task.WhenAll(Task[])"/>; exceptions surface per Task.WhenAll semantics.</summary>
    Parallel,
}
