using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.Domain.Events;
using Moongazing.OrionGuard.Domain.Primitives;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox;

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
            // Do NOT drain the aggregate here — leave the events on the aggregate so that if
            // EF's IExecutionStrategy retries the save after a transient fault, the next
            // SavingChanges invocation can still observe them. The collector merely holds a
            // reference; the actual pull happens in SavedChangesAsync (post-commit) via
            // DrainSnapshot().
            foreach (var aggregate in aggregates)
            {
                collector.TrackAggregate(aggregate);
            }
        }
        else
        {
            var current = Activity.Current;
            foreach (var aggregate in aggregates)
            {
                var events = aggregate.PullDomainEvents();
                foreach (var e in events)
                {
                    ctx.Add(new OutboxMessage
                    {
                        EventType = e.GetType().AssemblyQualifiedName!,
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
        // A transient save failure must not leave stale tracked aggregates in the collector for
        // the next save attempt (e.g. an IExecutionStrategy retry). Reset discards refs/events
        // without draining — the aggregate retains its domain events for the retry to observe.
        var collector = serviceProvider.GetRequiredService<DomainEventCollector>();
        collector.Reset();
        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }
}
