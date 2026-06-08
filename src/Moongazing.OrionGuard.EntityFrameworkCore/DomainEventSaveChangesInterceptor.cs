using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.Domain.Events;
using Moongazing.OrionGuard.Domain.Primitives;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Push;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox.TypeMap;

namespace Moongazing.OrionGuard.EntityFrameworkCore;

/// <summary>
/// Pulls events from tracked <see cref="IAggregateRoot"/> entities at SavingChanges and either
/// (a) dispatches them post-commit via <see cref="IDomainEventDispatcher"/> (<see cref="DomainEventDispatchStrategy.Inline"/>),
/// or (b) persists them as <see cref="OutboxMessage"/> rows in the same transaction
/// (<see cref="DomainEventDispatchStrategy.Outbox"/>).
/// </summary>
public sealed class DomainEventSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly IServiceProvider serviceProvider;

    /// <summary>Initializes a new instance of <see cref="DomainEventSaveChangesInterceptor"/>.</summary>
    /// <param name="serviceProvider">
    /// The DbContext's resolution-time scope provider — captured at DbContext construction by the
    /// <c>(sp, o) =&gt; o.AddInterceptors(new DomainEventSaveChangesInterceptor(sp))</c> wiring.
    /// Used to resolve <see cref="DomainEventCollector"/> (Scoped), <see cref="OrionGuardEfCoreOptions"/>
    /// (Singleton), and <see cref="IDomainEventDispatcher"/> (Scoped) inside the interceptor.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="serviceProvider"/> is null.</exception>
    public DomainEventSaveChangesInterceptor(IServiceProvider serviceProvider)
    {
        this.serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    /// <inheritdoc />
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        var ctx = eventData.Context!;
        var options = serviceProvider.GetRequiredService<OrionGuardEfCoreOptions>();
        var collector = serviceProvider.GetRequiredService<DomainEventCollector>();

        var aggregates = ctx.ChangeTracker.Entries<IAggregateRoot>().Select(e => e.Entity).ToList();
        if (aggregates.Count == 0)
        {
            return await base.SavingChangesAsync(eventData, result, cancellationToken).ConfigureAwait(false);
        }

        if (options.Strategy == DomainEventDispatchStrategy.Inline)
        {
            // Why: do NOT drain the aggregate here. Leaving events on the aggregate lets an
            // IExecutionStrategy retry (post transient fault) re-observe them on the next
            // SavingChanges invocation. The actual pull happens post-commit in SavedChangesAsync.
            foreach (var aggregate in aggregates)
            {
                collector.TrackAggregate(aggregate);
            }
        }
        else
        {
            var current = Activity.Current;
            var typeMap = serviceProvider.GetService<OutboxTypeMapRegistry>();
            foreach (var aggregate in aggregates)
            {
                var events = aggregate.PullDomainEvents();
                foreach (var e in events)
                {
                    ctx.Add(new OutboxMessage
                    {
                        EventType = ResolveEventTypeId(e.GetType(), typeMap),
                        Payload = JsonSerializer.Serialize(e, e.GetType()),
                        OccurredOnUtc = e.OccurredOnUtc,
                        TraceParent = current?.Id,
                        TraceState = current?.TraceStateString,
                    });
                }
            }
        }
        return await base.SavingChangesAsync(eventData, result, cancellationToken).ConfigureAwait(false);
    }

    private static string ResolveEventTypeId(Type eventType, OutboxTypeMapRegistry? typeMap)
    {
        if (typeMap is not null && typeMap.TryGetLogicalName(eventType, out var logical))
        {
            return logical;
        }
        return eventType.AssemblyQualifiedName ?? eventType.FullName!;
    }

    /// <inheritdoc />
    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        var options = serviceProvider.GetRequiredService<OrionGuardEfCoreOptions>();
        if (options.Strategy != DomainEventDispatchStrategy.Inline)
        {
            // Outbox mode: fire the optional push-dispatch wake so a ChannelOutboxWakeSignal
            // or other push backend can wake the dispatcher mid-poll instead of waiting for
            // the polling interval. The default NullOutboxWakeSignal makes this a no-op.
            //
            // Use CancellationToken.None: the rows are already committed by the time we get
            // here, so a request-level cancellation MUST NOT skip the wake. Skipping would
            // leave the dispatcher waiting up to PollingInterval before noticing the new rows,
            // which is exactly what the push-dispatch contract exists to prevent.
            var wakeSignal = serviceProvider.GetService<IOutboxWakeSignal>();
            if (wakeSignal is not null)
            {
                await wakeSignal.SignalAsync(CancellationToken.None).ConfigureAwait(false);
            }
            return await base.SavedChangesAsync(eventData, result, cancellationToken).ConfigureAwait(false);
        }

        var collector = serviceProvider.GetRequiredService<DomainEventCollector>();
        var pending = collector.DrainSnapshot();
        if (pending.Count == 0)
        {
            return await base.SavedChangesAsync(eventData, result, cancellationToken).ConfigureAwait(false);
        }

        var dispatcher = serviceProvider.GetRequiredService<IDomainEventDispatcher>();
        foreach (var e in pending)
        {
            await dispatcher.DispatchAsync(e, cancellationToken).ConfigureAwait(false);
        }

        return await base.SavedChangesAsync(eventData, result, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        // Why: a transient save failure must not leave stale tracked aggregates in the collector
        // for the next save attempt (e.g. an IExecutionStrategy retry). Reset discards references
        // without draining, so the aggregate retains its domain events for the retry to observe.
        var collector = serviceProvider.GetRequiredService<DomainEventCollector>();
        collector.Reset();
        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }
}
