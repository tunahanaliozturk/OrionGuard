using Moongazing.OrionGuard.EntityFrameworkCore.Outbox;

namespace Moongazing.OrionGuard.EntityFrameworkCore;

/// <summary>Selects how the SaveChanges interceptor surfaces domain events.</summary>
public enum DomainEventDispatchStrategy
{
    /// <summary>Dispatch synchronously after a successful save (default).</summary>
    Inline,
    /// <summary>Persist events to the outbox table; a hosted worker dispatches them asynchronously.</summary>
    Outbox,
}

/// <summary>Configures OrionGuard's EF Core integration.</summary>
public sealed class OrionGuardEfCoreOptions
{
    /// <summary>Currently selected strategy. Default <see cref="DomainEventDispatchStrategy.Inline"/>.</summary>
    public DomainEventDispatchStrategy Strategy { get; private set; } = DomainEventDispatchStrategy.Inline;

    /// <summary>Outbox configuration (only consulted when <see cref="Strategy"/> is <see cref="DomainEventDispatchStrategy.Outbox"/>).</summary>
    public OutboxOptions Outbox { get; private set; } = new();

    /// <summary>Selects Inline mode (post-save dispatch).</summary>
    public OrionGuardEfCoreOptions UseInline()
    {
        Strategy = DomainEventDispatchStrategy.Inline;
        return this;
    }

    /// <summary>Selects Outbox mode and optionally configures it.</summary>
    public OrionGuardEfCoreOptions UseOutbox(Action<OutboxOptions>? configure = null)
    {
        Strategy = DomainEventDispatchStrategy.Outbox;
        if (configure is not null)
        {
            var temp = new OutboxOptions();
            configure(temp);
            Outbox = temp;
        }
        return this;
    }
}
