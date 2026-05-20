using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Locking;

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

    internal List<Action<IServiceCollection>> ServiceCustomizations { get; } = new();

    /// <summary>
    /// Replaces the registered <see cref="IDistributedLock"/> implementation. Default is
    /// <see cref="SkipLockedDistributedLock"/>; alternatives include <see cref="NullDistributedLock"/>
    /// or a custom (e.g. Redis) implementation.
    /// </summary>
    public OrionGuardEfCoreOptions UseDistributedLock<TLock>() where TLock : class, IDistributedLock
    {
        ServiceCustomizations.Add(services =>
            services.Replace(ServiceDescriptor.Singleton<IDistributedLock, TLock>()));
        return this;
    }
}
