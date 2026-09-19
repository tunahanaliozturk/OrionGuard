using System.Data.Common;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
/// <remarks>
/// <para>
/// Both <c>SaveChanges()</c> and <c>SaveChangesAsync()</c> are intercepted. A synchronous save in Inline mode
/// dispatches on a thread-pool thread and blocks until the handlers finish, so prefer
/// <c>SaveChangesAsync()</c> where handlers do I/O.
/// </para>
/// <para>
/// Inline mode inside a transaction started with <c>Database.BeginTransaction()</c> holds the events until that
/// transaction commits and drops them if it rolls back. EF Core cannot observe the commit of an ambient
/// <see cref="System.Transactions.TransactionScope"/> or of a transaction passed in with
/// <c>Database.UseTransaction()</c>, so there the events are still dispatched right after the save, before the
/// commit, and a warning is logged once; use Outbox mode for those.
/// </para>
/// <para>
/// The interceptor also implements <see cref="IDbTransactionInterceptor"/> to observe those commits, so it must be
/// added as a whole (<c>AddInterceptors(interceptor)</c> or <c>UseOrionGuardDomainEvents(sp)</c>).
/// </para>
/// </remarks>
public sealed class DomainEventSaveChangesInterceptor : SaveChangesInterceptor, IDbTransactionInterceptor
{
    // W3C trace-context: a tracestate list member longer than this is the first thing dropped when truncating.
    private const int MaxTraceStateMemberLength = 128;

    private static int dispatchBeforeCommitWarned;

    private readonly IServiceProvider serviceProvider;
    private readonly bool isRootProvider;

    // Keyed by DbContext instance: with AddDbContextPool or AddDbContextFactory a single interceptor serves every
    // context in the process, so no per-save state may live on the interceptor itself.
    private readonly ConditionalWeakTable<DbContext, SaveState> states = new();

    /// <summary>Initializes a new instance of <see cref="DomainEventSaveChangesInterceptor"/>.</summary>
    /// <param name="serviceProvider">
    /// The provider handed to the options callback of <c>AddDbContext&lt;T&gt;((sp, o) =&gt; ...)</c>, used to
    /// resolve <see cref="OrionGuardEfCoreOptions"/>, <see cref="IDomainEventDispatcher"/> and the optional outbox
    /// services. With <c>AddDbContext</c> it is the request scope. With <c>AddDbContextPool</c> or
    /// <c>AddDbContextFactory</c> it is the root provider; the interceptor then creates a new scope for each
    /// Inline dispatch instead of resolving scoped services from the root.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="serviceProvider"/> is null.</exception>
    public DomainEventSaveChangesInterceptor(IServiceProvider serviceProvider)
    {
        this.serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        isRootProvider = IsRootProvider(serviceProvider);
    }

    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        BeginSave(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        BeginSave(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    /// <inheritdoc />
    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        var events = CompleteSave(eventData.Context, out var wakeSignal);
        if (wakeSignal is not null)
        {
            SignalWithoutBlocking(wakeSignal);
        }
        DispatchBlocking(events);
        return base.SavedChanges(eventData, result);
    }

    /// <inheritdoc />
    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        var events = CompleteSave(eventData.Context, out var wakeSignal);
        if (wakeSignal is not null)
        {
            // CancellationToken.None: the rows are committed, so a cancelled request must not skip the wake and
            // leave the dispatcher waiting for the next polling interval.
            await wakeSignal.SignalAsync(CancellationToken.None).ConfigureAwait(false);
        }
        await DispatchAsync(events, cancellationToken).ConfigureAwait(false);
        return await base.SavedChangesAsync(eventData, result, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        AbandonSave(eventData.Context);
        base.SaveChangesFailed(eventData);
    }

    /// <inheritdoc />
    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        AbandonSave(eventData.Context);
        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    /// <inheritdoc />
    public override void SaveChangesCanceled(DbContextEventData eventData)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        AbandonSave(eventData.Context);
        base.SaveChangesCanceled(eventData);
    }

    /// <inheritdoc />
    public override Task SaveChangesCanceledAsync(DbContextEventData eventData, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        AbandonSave(eventData.Context);
        return base.SaveChangesCanceledAsync(eventData, cancellationToken);
    }

    DbTransaction IDbTransactionInterceptor.TransactionStarted(
        DbConnection connection, TransactionEndEventData eventData, DbTransaction result)
    {
        TrackTransaction(eventData?.Context, result);
        return result;
    }

    ValueTask<DbTransaction> IDbTransactionInterceptor.TransactionStartedAsync(
        DbConnection connection, TransactionEndEventData eventData, DbTransaction result, CancellationToken cancellationToken)
    {
        TrackTransaction(eventData?.Context, result);
        return ValueTask.FromResult(result);
    }

    void IDbTransactionInterceptor.TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData)
        => DispatchBlocking(TakeEventsAwaitingCommit(eventData?.Context, transaction));

    Task IDbTransactionInterceptor.TransactionCommittedAsync(
        DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken)
        => DispatchAsync(TakeEventsAwaitingCommit(eventData?.Context, transaction), cancellationToken);

    void IDbTransactionInterceptor.TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData)
        => DiscardEventsAwaitingCommit(eventData?.Context);

    Task IDbTransactionInterceptor.TransactionRolledBackAsync(
        DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken)
    {
        DiscardEventsAwaitingCommit(eventData?.Context);
        return Task.CompletedTask;
    }

    void IDbTransactionInterceptor.TransactionFailed(DbTransaction transaction, TransactionErrorEventData eventData)
    {
        if (EndsTransaction(eventData))
        {
            DiscardEventsAwaitingCommit(eventData.Context);
        }
    }

    Task IDbTransactionInterceptor.TransactionFailedAsync(
        DbTransaction transaction, TransactionErrorEventData eventData, CancellationToken cancellationToken)
    {
        if (EndsTransaction(eventData))
        {
            DiscardEventsAwaitingCommit(eventData.Context);
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Fits a W3C <c>tracestate</c> header into <paramref name="maxLength"/> characters. Members longer than 128
    /// characters are dropped first, then whole members from the end, so every member that is kept stays intact.
    /// Returns <see langword="null"/> when no member fits.
    /// </summary>
    internal static string? FitTraceState(string? traceState, int? maxLength)
    {
        if (string.IsNullOrEmpty(traceState) || maxLength is null || traceState.Length <= maxLength)
        {
            return traceState;
        }

        var kept = new StringBuilder(maxLength.Value);
        foreach (var member in traceState.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (member.Length > MaxTraceStateMemberLength)
            {
                continue;
            }
            var separator = kept.Length == 0 ? 0 : 1;
            if (kept.Length + separator + member.Length > maxLength)
            {
                break;
            }
            if (separator == 1)
            {
                kept.Append(',');
            }
            kept.Append(member);
        }
        return kept.Length == 0 ? null : kept.ToString();
    }

    private void BeginSave(DbContext? context)
    {
        if (context is null)
        {
            return;
        }
        var state = states.GetOrCreateValue(context);
        state.Aggregates = null;
        state.EnqueuedRows = 0;

        var aggregates = context.ChangeTracker.Entries<IAggregateRoot>().Select(e => e.Entity).ToList();
        if (aggregates.Count == 0)
        {
            return;
        }

        var options = serviceProvider.GetRequiredService<OrionGuardEfCoreOptions>();
        if (options.Strategy == DomainEventDispatchStrategy.Inline)
        {
            // Leave the events on the aggregates: an execution-strategy retry re-runs SavingChanges after a
            // transient fault and must still find them. They are pulled once the save has succeeded.
            state.Aggregates = aggregates;
            return;
        }

        var outboxType = context.Model.FindEntityType(typeof(OutboxMessage));
        var (traceParent, traceState) = CaptureTraceContext(outboxType);
        var typeMap = serviceProvider.GetService<OutboxTypeMapRegistry>();
        foreach (var aggregate in aggregates)
        {
            foreach (var e in aggregate.PullDomainEvents())
            {
                context.Add(new OutboxMessage
                {
                    EventType = ResolveEventTypeId(e.GetType(), typeMap),
                    Payload = JsonSerializer.Serialize(e, e.GetType()),
                    OccurredOnUtc = e.OccurredOnUtc,
                    TraceParent = traceParent,
                    TraceState = traceState,
                });
                state.EnqueuedRows++;
            }
        }
    }

    // Returns the events Inline mode must dispatch now. Outbox saves, and Inline saves inside a transaction whose
    // commit this interceptor can observe, return none.
    private IReadOnlyList<IDomainEvent> CompleteSave(DbContext? context, out IOutboxWakeSignal? wakeSignal)
    {
        wakeSignal = null;
        if (context is null)
        {
            return Array.Empty<IDomainEvent>();
        }
        var state = states.GetOrCreateValue(context);
        var options = serviceProvider.GetRequiredService<OrionGuardEfCoreOptions>();
        if (options.Strategy != DomainEventDispatchStrategy.Inline)
        {
            // Recorded only now that the save succeeded, so a failed save never counts phantom rows.
            OutboxDispatcherDiagnostics.RecordEnqueuedRowsPerSave(state.EnqueuedRows);
            state.EnqueuedRows = 0;
            wakeSignal = serviceProvider.GetService<IOutboxWakeSignal>();
            return Array.Empty<IDomainEvent>();
        }

        var inTrackedTransaction = IsInTrackedTransaction(context, state);
        if (!inTrackedTransaction)
        {
            // Anything still awaiting a commit belongs to a transaction that ended without a commit this
            // interceptor saw (it was disposed), so it was never committed.
            state.AwaitingCommit.Clear();
            state.Transaction = null;
        }

        var events = TakeSavedEvents(state);
        if (events.Count == 0)
        {
            return events;
        }
        if (inTrackedTransaction)
        {
            state.AwaitingCommit.AddRange(events);
            return Array.Empty<IDomainEvent>();
        }
        if (context.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null)
        {
            WarnDispatchBeforeCommit(context);
        }
        return events;
    }

    private List<IDomainEvent> TakeSavedEvents(SaveState state)
    {
        var events = new List<IDomainEvent>();
        // Events a caller queued by hand on the scoped collector come first, as they always have. The root
        // provider has no scope to hold a collector, so it is skipped there.
        if (!isRootProvider && serviceProvider.GetService<DomainEventCollector>() is { } collector)
        {
            events.AddRange(collector.DrainSnapshot());
        }
        if (state.Aggregates is { } aggregates)
        {
            // Taken before dispatching: a handler that saves this same context starts a fresh save.
            state.Aggregates = null;
            foreach (var aggregate in aggregates)
            {
                events.AddRange(aggregate.PullDomainEvents());
            }
        }
        return events;
    }

    private void AbandonSave(DbContext? context)
    {
        // A failed or cancelled save leaves the events on the aggregates for the next attempt to pick up.
        if (context is not null && states.TryGetValue(context, out var state))
        {
            state.Aggregates = null;
            state.EnqueuedRows = 0;
        }
        if (!isRootProvider)
        {
            serviceProvider.GetService<DomainEventCollector>()?.Reset();
        }
    }

    private void TrackTransaction(DbContext? context, DbTransaction transaction)
    {
        if (context is null)
        {
            return;
        }
        var state = states.GetOrCreateValue(context);
        // A new transaction means the previous one ended; if it ended without a commit or rollback this
        // interceptor saw (a Dispose), its events were never committed.
        state.AwaitingCommit.Clear();
        state.Transaction = transaction;
    }

    private IReadOnlyList<IDomainEvent> TakeEventsAwaitingCommit(DbContext? context, DbTransaction transaction)
    {
        if (context is null || !states.TryGetValue(context, out var state) || !ReferenceEquals(state.Transaction, transaction))
        {
            return Array.Empty<IDomainEvent>();
        }
        state.Transaction = null;
        if (state.AwaitingCommit.Count == 0)
        {
            return Array.Empty<IDomainEvent>();
        }
        var events = state.AwaitingCommit.ToArray();
        state.AwaitingCommit.Clear();
        return events;
    }

    private void DiscardEventsAwaitingCommit(DbContext? context)
    {
        if (context is not null && states.TryGetValue(context, out var state))
        {
            state.AwaitingCommit.Clear();
            state.Transaction = null;
        }
    }

    private static bool EndsTransaction(TransactionErrorEventData? eventData)
        => eventData?.Action is "Commit" or "Rollback";

    private static bool IsInTrackedTransaction(DbContext context, SaveState state)
        => state.Transaction is not null
           && context.Database.CurrentTransaction is IInfrastructure<DbTransaction> current
           && ReferenceEquals(current.Instance, state.Transaction);

    private async Task DispatchAsync(IReadOnlyList<IDomainEvent> events, CancellationToken cancellationToken)
    {
        if (events.Count == 0)
        {
            return;
        }
        if (!isRootProvider)
        {
            await DispatchAllAsync(serviceProvider.GetRequiredService<IDomainEventDispatcher>(), events, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        // The root provider must not hand out scoped handlers (they would be shared process-wide), so each
        // dispatch gets its own scope.
        await using var scope = serviceProvider.CreateAsyncScope();
        await DispatchAllAsync(scope.ServiceProvider.GetRequiredService<IDomainEventDispatcher>(), events, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task DispatchAllAsync(
        IDomainEventDispatcher dispatcher, IReadOnlyList<IDomainEvent> events, CancellationToken cancellationToken)
    {
        foreach (var e in events)
        {
            await dispatcher.DispatchAsync(e, cancellationToken).ConfigureAwait(false);
        }
    }

    // Synchronous SaveChanges/Commit have no async path to await the handlers on. Running the dispatch on the
    // thread pool keeps a caller's SynchronizationContext out of the handlers' continuations, so blocking here
    // cannot deadlock on it; handler exceptions surface unwrapped, as on the async path.
    private void DispatchBlocking(IReadOnlyList<IDomainEvent> events)
    {
        if (events.Count > 0)
        {
            Task.Run(() => DispatchAsync(events, CancellationToken.None)).GetAwaiter().GetResult();
        }
    }

    private static void SignalWithoutBlocking(IOutboxWakeSignal wakeSignal)
    {
        // The wake only shortens dispatch latency (PollingInterval still bounds it), so a synchronous save does
        // not wait for a signal that completes asynchronously; a fault is observed and dropped.
        var signal = wakeSignal.SignalAsync(CancellationToken.None);
        if (!signal.IsCompletedSuccessfully)
        {
            _ = signal.AsTask().ContinueWith(
                static t => _ = t.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    private (string? TraceParent, string? TraceState) CaptureTraceContext(IEntityType? outboxType)
    {
        // Only a W3C id is a traceparent; a hierarchical id (legacy Request-Id) can run past the column and is not
        // something the dispatcher could parse back anyway. Telemetry must never fail the business save, so
        // anything that does not fit its column is trimmed or dropped.
        var current = Activity.Current;
        if (current is null || current.IdFormat != ActivityIdFormat.W3C)
        {
            return (null, null);
        }
        var traceParent = current.Id;
        if (traceParent is null || traceParent.Length > (MaxLength(outboxType, nameof(OutboxMessage.TraceParent)) ?? int.MaxValue))
        {
            return (null, null);
        }
        return (traceParent, FitTraceState(current.TraceStateString, MaxLength(outboxType, nameof(OutboxMessage.TraceState))));
    }

    private static int? MaxLength(IEntityType? entityType, string propertyName)
        => entityType?.FindProperty(propertyName)?.GetMaxLength();

    private static string ResolveEventTypeId(Type eventType, OutboxTypeMapRegistry? typeMap)
    {
        if (typeMap is not null && typeMap.TryGetLogicalName(eventType, out var logical))
        {
            return logical;
        }
        return eventType.AssemblyQualifiedName ?? eventType.FullName!;
    }

    private void WarnDispatchBeforeCommit(DbContext context)
    {
        if (Interlocked.Exchange(ref dispatchBeforeCommitWarned, 1) != 0)
        {
            return;
        }
        serviceProvider.GetService<ILoggerFactory>()?.CreateLogger<DomainEventSaveChangesInterceptor>().LogWarning(
            "Inline domain events of {DbContext} were dispatched before the surrounding transaction committed: EF Core " +
            "cannot observe the commit of an ambient TransactionScope or of a transaction passed to UseTransaction. " +
            "Start the transaction with Database.BeginTransaction() or use Outbox mode. This warning is logged once.",
            context.GetType().Name);
    }

    // Microsoft.Extensions.DependencyInjection resolves IServiceScopeFactory to the root scope, so only the root
    // hands back itself; AddDbContextPool and AddDbContextFactory pass that root to the options callback. Other
    // containers are treated as scoped, which is the behaviour this interceptor always had.
    private static bool IsRootProvider(IServiceProvider provider)
        => provider is ServiceProvider
           || ReferenceEquals(provider.GetService(typeof(IServiceScopeFactory)), provider);

    private sealed class SaveState
    {
        public List<IAggregateRoot>? Aggregates;
        public int EnqueuedRows;
        public DbTransaction? Transaction;
        public readonly List<IDomainEvent> AwaitingCommit = new();
    }
}
