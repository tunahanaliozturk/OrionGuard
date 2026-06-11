using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Archival;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Locking;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox.TypeMap;

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

    /// <summary>
    /// Configures the outbox <see cref="OutboxTypeMapRegistry"/> so events can be persisted under
    /// stable logical names instead of their assembly-qualified CLR names. Optional — without this
    /// call the registry stays empty and the dispatcher falls back to AQN resolution.
    /// </summary>
    public OrionGuardEfCoreOptions UseOutboxTypeMap(
        Action<OutboxTypeMapRegistry> configure,
        Action<OutboxTypeMapOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var registry = new OutboxTypeMapRegistry();
        configure(registry);

        var options = new OutboxTypeMapOptions();
        configureOptions?.Invoke(options);

        ServiceCustomizations.Add(services =>
        {
            services.Replace(ServiceDescriptor.Singleton(registry));
            services.Replace(ServiceDescriptor.Singleton(options));
        });
        return this;
    }

    /// <summary>
    /// Enables the opt-in <see cref="OutboxArchivalHostedService"/>. Without this call no archival
    /// hosted service is registered and processed outbox rows accumulate indefinitely.
    /// </summary>
    public OrionGuardEfCoreOptions UseOutboxArchival(Action<OutboxArchivalOptions>? configure = null)
    {
        var options = new OutboxArchivalOptions();
        configure?.Invoke(options);

        ServiceCustomizations.Add(services =>
        {
            services.Replace(ServiceDescriptor.Singleton(options));
            // v6.5.14: register the optional liveness mirror as a singleton so the
            // OutboxArchivalHealthCheck can observe RecordSuccessfulBatch updates.
            // The hosted service resolves it via the new 6-arg constructor path.
            services.TryAddSingleton<OutboxArchivalState>();
            // Register the default health-check options so consumers can wire the check
            // with AddCheck<OutboxArchivalHealthCheck>(...) without also writing
            // services.AddSingleton(new OutboxArchivalHealthCheckOptions()) by hand.
            services.TryAddSingleton<OutboxArchivalHealthCheckOptions>();
            // Force the DI container to pick the 6-arg state-aware constructor by
            // registering the hosted service via a factory that explicitly resolves
            // OutboxArchivalState. Otherwise MS.DI falls back to the 4-arg ctor (which
            // hard-codes state: null) when IOutboxArchiver is not registered.
            services.AddHostedService(sp => new OutboxArchivalHostedService(
                sp.GetRequiredService<OutboxArchivalOptions>(),
                sp.GetRequiredService<IServiceScopeFactory>(),
                sp.GetRequiredService<IDistributedLock>(),
                sp.GetService<Microsoft.Extensions.Logging.ILogger<OutboxArchivalHostedService>>(),
                sp.GetService<IOutboxArchiver>(),
                sp.GetRequiredService<OutboxArchivalState>()));
        });
        return this;
    }
}
