using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moongazing.OrionGuard.Domain.Events;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Locking;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Push;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox.TypeMap;

namespace Moongazing.OrionGuard.EntityFrameworkCore.Outbox;

/// <summary>
/// Polls <see cref="OutboxMessage"/> rows from the consumer's <see cref="DbContext"/>, deserializes
/// each event by its <see cref="OutboxMessage.EventType"/>, and dispatches via
/// <see cref="IDomainEventDispatcher"/>. On dispatch failure, increments <see cref="OutboxMessage.RetryCount"/>;
/// after <see cref="OutboxOptions.MaxRetries"/> attempts the row is dead-lettered (marked processed).
/// </summary>
/// <remarks>
/// <para>
/// Multi-instance safety: at startup the worker resolves an <see cref="IDistributedLock"/>
/// (default <see cref="SkipLockedDistributedLock"/>) and acquires <see cref="OutboxOptions.LockKey"/>
/// before each batch. Instances that fail to acquire the lock sleep and retry, so only one replica
/// dispatches at a time. Pin to <see cref="NullDistributedLock"/> for single-instance deployments
/// that do not want to apply the <c>OrionGuard_OutboxLocks</c> migration.
/// </para>
/// <para>
/// Each row is dispatched in a DI scope of its own. A handler that writes through the scoped
/// <see cref="DbContext"/> has those writes committed together with the row's processed stamp; if the handler
/// throws, or its writes cannot be saved, they are discarded and the failure is recorded on the row, where it
/// counts towards <see cref="OutboxOptions.MaxRetries"/>. Every row update, success included, is conditional on
/// the state the row was read in (unprocessed, same <see cref="OutboxMessage.RetryCount"/> and
/// <see cref="OutboxMessage.Error"/>), so a replay or discard made while the row is being dispatched is not
/// overwritten; a dispatch that loses to one also rolls back its handler's writes.
/// </para>
/// </remarks>
public sealed class OutboxDispatcherHostedService : BackgroundService
{
    private const string UnreferencedCodeMessage = "Type.GetType deserializes events by assembly-qualified name; event types must be preserved in the AOT build (e.g. via DynamicDependency or by rooting them in your application). System.Text.Json source generation is recommended for full AOT support.";
    private const string DynamicCodeMessage = "JsonSerializer.Deserialize(string, Type) requires runtime code generation under AOT. Use System.Text.Json source generation for full AOT support.";

    private static readonly ActivitySource OutboxActivitySource = new("Moongazing.OrionGuard.DomainEvents", MeterVersion.Value);

    private readonly OutboxOptions options;
    private readonly IServiceScopeFactory scopeFactory;
    private readonly IDistributedLock distributedLock;
    private readonly OutboxTypeMapRegistry typeMap;
    private readonly OutboxTypeMapOptions typeMapOptions;
    private readonly IOutboxWakeSignal wakeSignal;
    private readonly ILogger<OutboxDispatcherHostedService>? logger;
    // Null and NullOutboxRowFailureObserver both mean 'no observer', so the dispatcher skips the call entirely.
    private readonly IOutboxRowFailureObserver? rowFailureObserver;
    private readonly TimeProvider timeProvider;
    // 1 once the assembly-qualified-name fallback warning has been logged; set with Interlocked.
    private int assemblyQualifiedNameFallbackWarned;

    /// <summary>Initializes a new worker.</summary>
    /// <param name="options">Outbox dispatch configuration.</param>
    /// <param name="scopeFactory">Factory used to create the DI scopes that resolve <see cref="DbContext"/> and <see cref="IDomainEventDispatcher"/>.</param>
    /// <param name="distributedLock">
    /// Distributed lock used to coordinate dispatcher instances. When <see langword="null"/>, a
    /// <see cref="NullDistributedLock"/> is used (single-instance behaviour).
    /// </param>
    /// <param name="typeMap">
    /// Logical-name registry consulted when resolving <see cref="OutboxMessage.EventType"/>. When
    /// <see langword="null"/>, an empty registry is used and resolution falls back to AQN per
    /// <paramref name="typeMapOptions"/>.
    /// </param>
    /// <param name="typeMapOptions">
    /// Controls the AQN fallback behaviour when the registry has no mapping. When <see langword="null"/>,
    /// defaults preserve v6.3 source compatibility (AQN fallback enabled).
    /// </param>
    /// <param name="wakeSignal">
    /// Optional push-based wake signal used to wake the dispatcher mid-poll when new rows arrive.
    /// When <see langword="null"/>, defaults to <see cref="NullOutboxWakeSignal"/> (polling only).
    /// </param>
    /// <param name="logger">Optional logger used to surface startup, per-row and per-poll diagnostic messages.</param>
    /// <param name="rowFailureObserver">
    /// Optional observer notified for every failed row attempt (transient and terminal). Defaults to no-op when
    /// null or <see cref="NullOutboxRowFailureObserver"/> is supplied.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> or <paramref name="scopeFactory"/> is <see langword="null"/>.</exception>
    public OutboxDispatcherHostedService(
        OutboxOptions options,
        IServiceScopeFactory scopeFactory,
        IDistributedLock? distributedLock = null,
        OutboxTypeMapRegistry? typeMap = null,
        OutboxTypeMapOptions? typeMapOptions = null,
        ILogger<OutboxDispatcherHostedService>? logger = null,
        IOutboxWakeSignal? wakeSignal = null,
        IOutboxRowFailureObserver? rowFailureObserver = null)
        : this(options, scopeFactory, distributedLock, typeMap, typeMapOptions, logger, wakeSignal, rowFailureObserver, timeProvider: null)
    {
    }

    /// <summary>Initializes a new worker that reads the current time from <paramref name="timeProvider"/>.</summary>
    /// <param name="options">Outbox dispatch configuration.</param>
    /// <param name="scopeFactory">Factory used to create the DI scopes that resolve <see cref="DbContext"/> and <see cref="IDomainEventDispatcher"/>.</param>
    /// <param name="distributedLock">Distributed lock; <see langword="null"/> means <see cref="NullDistributedLock"/>.</param>
    /// <param name="typeMap">Logical-name registry; <see langword="null"/> means an empty registry.</param>
    /// <param name="typeMapOptions">AQN fallback settings; <see langword="null"/> means the defaults.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="wakeSignal">Optional wake signal; <see langword="null"/> means <see cref="NullOutboxWakeSignal"/>.</param>
    /// <param name="rowFailureObserver">Optional row failure observer.</param>
    /// <param name="timeProvider">
    /// Clock used for <see cref="OutboxMessage.ProcessedOnUtc"/> and the queue-lag metric;
    /// <see langword="null"/> means <see cref="TimeProvider.System"/>.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> or <paramref name="scopeFactory"/> is <see langword="null"/>.</exception>
    public OutboxDispatcherHostedService(
        OutboxOptions options,
        IServiceScopeFactory scopeFactory,
        IDistributedLock? distributedLock,
        OutboxTypeMapRegistry? typeMap,
        OutboxTypeMapOptions? typeMapOptions,
        ILogger<OutboxDispatcherHostedService>? logger,
        IOutboxWakeSignal? wakeSignal,
        IOutboxRowFailureObserver? rowFailureObserver,
        TimeProvider? timeProvider)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        this.distributedLock = distributedLock ?? new NullDistributedLock();
        this.typeMap = typeMap ?? new OutboxTypeMapRegistry();
        this.typeMapOptions = typeMapOptions ?? new OutboxTypeMapOptions();
        this.logger = logger;
        this.wakeSignal = wakeSignal ?? new NullOutboxWakeSignal();
        this.rowFailureObserver = rowFailureObserver is NullOutboxRowFailureObserver ? null : rowFailureObserver;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "A failed poll is logged and retried on the next interval; per-row faults are recorded on the OutboxMessage row.")]
    [RequiresUnreferencedCode(UnreferencedCodeMessage)]
    [RequiresDynamicCode(DynamicCodeMessage)]
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger?.LogInformation(
            "OrionGuard outbox dispatcher started with distributed locking key '{LockKey}' (lease {Lease}).",
            options.LockKey, options.LockLeaseDuration);

        while (!stoppingToken.IsCancellationRequested)
        {
            var drainedFullBatch = false;
            try
            {
                // Another instance holding the lease (or a lock that is unavailable) returns null: skip this poll.
                // The lock records lock_contended itself on genuine contention only.
                await using var handle = await distributedLock.TryAcquireAsync(
                    options.LockKey,
                    options.LockLeaseDuration,
                    stoppingToken).ConfigureAwait(false);

                if (handle is not null)
                {
                    drainedFullBatch = await ProcessBatchCoreAsync(stoppingToken).ConfigureAwait(false) >= options.BatchSize;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // The worker survives infrastructure faults (database down, table missing, misconfigured DI) and
                // retries on the next poll, so they must be visible here rather than swallowed.
                OutboxDispatcherDiagnostics.RecordBatchFault(ex.GetType().Name);
                logger?.LogError(ex, "Outbox dispatch poll failed; retrying in {PollingInterval}.", options.PollingInterval);
            }

            // Every row of a full batch left the queue, so more are probably waiting: poll again straight away
            // instead of capping throughput at BatchSize rows per PollingInterval. A batch with a transient failure
            // waits, so a failing row is retried once per interval rather than on every pass of the drain.
            if (drainedFullBatch)
            {
                continue;
            }

            try
            {
                await wakeSignal.WaitForNextTickAsync(options.PollingInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    /// <summary>Processes one batch of unprocessed outbox rows. Public for tests.</summary>
    /// <param name="cancellationToken">Token used to observe cancellation requests.</param>
    /// <returns>A task that completes when every row of the batch has been dispatched and its state saved.</returns>
    [RequiresUnreferencedCode(UnreferencedCodeMessage)]
    [RequiresDynamicCode(DynamicCodeMessage)]
    public Task ProcessBatchAsync(CancellationToken cancellationToken) => ProcessBatchCoreAsync(cancellationToken);

    /// <summary>
    /// Processes one batch and returns how many of its rows left the queue (dispatched, dead-lettered, or found
    /// already processed). Rows that failed transiently and will be retried are not counted.
    /// </summary>
    [RequiresUnreferencedCode(UnreferencedCodeMessage)]
    [RequiresDynamicCode(DynamicCodeMessage)]
    internal async Task<int> ProcessBatchCoreAsync(CancellationToken cancellationToken)
    {
        List<OutboxMessage> batch;
        await using (var readScope = scopeFactory.CreateAsyncScope())
        {
            // Read without tracking: every row is then handled in a scope of its own, so neither the batch nor a
            // handler's writes accumulate in one change tracker.
            batch = await readScope.ServiceProvider.GetRequiredService<DbContext>().Set<OutboxMessage>()
                .AsNoTracking()
                .Where(m => m.ProcessedOnUtc == null)
                .OrderBy(m => m.OccurredOnUtc)
                .Take(options.BatchSize)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        if (batch.Count == 0)
        {
            // Lets operators see what fraction of wake-ups found nothing and tune PollingInterval / BatchSize.
            OutboxDispatcherDiagnostics.RecordIdlePoll();
            return 0;
        }
        OutboxDispatcherDiagnostics.RecordDispatcherBatchSize(batch.Count);

        var completed = 0;
        foreach (var row in batch)
        {
            // A row whose handlers already ran is always stamped, even while stopping, but the rest of the batch
            // waits for the next start: dispatching it during shutdown risks being killed mid-row.
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            if (await ProcessRowAsync(row, cancellationToken).ConfigureAwait(false))
            {
                completed++;
            }
        }
        return completed;
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Per-row faults are recorded on the OutboxMessage row and drive the retry / dead-letter policy.")]
    [RequiresUnreferencedCode(UnreferencedCodeMessage)]
    [RequiresDynamicCode(DynamicCodeMessage)]
    private async Task<bool> ProcessRowAsync(OutboxMessage row, CancellationToken cancellationToken)
    {
        using var activity = StartDispatchActivity(row);
        await using var scope = scopeFactory.CreateAsyncScope();
        // Resolved outside the per-row catch: a missing registration is a configuration fault for the whole poll,
        // not a reason to burn every row's retries.
        var db = scope.ServiceProvider.GetRequiredService<DbContext>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDomainEventDispatcher>();

        IDomainEvent? @event;
        string? rejection;
        try
        {
            rejection = ReadEvent(row, out @event);
        }
        catch (Exception ex) when (!IsShutdown(ex, cancellationToken))
        {
            return await RecordFailureAsync(row, ex, ex.ToString(), cancellationToken).ConfigureAwait(false);
        }
        if (rejection is not null)
        {
            await DeadLetterAsync(db, row, rejection, cancellationToken).ConfigureAwait(false);
            return true;
        }

        Exception? dispatchFailure = null;
        var dispatchTimer = Stopwatch.StartNew();
        try
        {
            await dispatcher.DispatchAsync(@event!, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (!IsShutdown(ex, cancellationToken))
        {
            // Includes an OperationCanceledException from the handler itself (an HttpClient timeout, say): only
            // the stopping token means shutdown.
            dispatchFailure = ex;
        }
        finally
        {
            // Failing dispatches are timed too: slow failures are the most operator-relevant tail.
            OutboxDispatcherDiagnostics.RecordDispatchDuration(dispatchTimer.Elapsed.TotalMilliseconds);
        }
        if (dispatchFailure is not null)
        {
            return await RecordFailureAsync(row, dispatchFailure, dispatchFailure.ToString(), cancellationToken).ConfigureAwait(false);
        }

        var processedOnUtc = UtcNow();
        bool marked;
        try
        {
            // The handlers already ran. Abandoning the processed stamp because the host is shutting down
            // would dispatch this row again on the next start, so the write is not cancellable; the
            // database command timeout and the host's shutdown timeout still bound it.
            marked = await MarkProcessedAsync(db, row, processedOnUtc, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (!IsShutdown(ex, cancellationToken))
        {
            return await RecordFailureAsync(
                row,
                ex,
                $"The event was dispatched, but saving the handler's changes and the processed state failed: {ex}",
                cancellationToken).ConfigureAwait(false);
        }

        if (!marked)
        {
            logger?.LogInformation(
                "Outbox row {RowId} was dispatched, but meanwhile it had been replayed, discarded or processed elsewhere; its state was left as found and the handler's database writes were rolled back.",
                row.Id);
            return true;
        }

        // Recorded only once the processed state is persisted, so a failed save never double-counts a row that
        // will be dispatched again.
        OutboxDispatcherDiagnostics.RecordQueueLag((processedOnUtc - row.OccurredOnUtc).TotalMilliseconds);
        OutboxDispatcherDiagnostics.RecordRowPayloadSize(row.Payload?.Length ?? 0);
        OutboxDispatcherDiagnostics.RecordRetriesBeforeSuccess(row.RetryCount);
        return true;
    }

    [RequiresUnreferencedCode(UnreferencedCodeMessage)]
    [RequiresDynamicCode(DynamicCodeMessage)]
    private string? ReadEvent(OutboxMessage row, out IDomainEvent? @event)
    {
        @event = null;
        // Registry first, then the assembly-qualified name when the fallback is on: the registry decouples stored
        // rows from CLR type identity, the fallback keeps rows written before a mapping existed readable.
        Type? type;
        if (typeMap.TryResolve(row.EventType, out var resolved))
        {
            type = resolved;
        }
        else if (typeMapOptions.AllowAssemblyQualifiedNameFallback)
        {
            type = Type.GetType(row.EventType);
            if (type is not null)
            {
                WarnOnFirstAssemblyQualifiedNameFallback(row.EventType);
            }
        }
        else
        {
            type = null;
        }

        if (type is null)
        {
            return $"TYPE_NOT_FOUND: cannot resolve event type '{row.EventType}'. " +
                   $"Registry has no mapping and AQN fallback is " +
                   $"{(typeMapOptions.AllowAssemblyQualifiedNameFallback ? "enabled but resolution failed" : "disabled")}.";
        }
        if (!typeof(IDomainEvent).IsAssignableFrom(type))
        {
            return $"TYPE_NOT_DOMAIN_EVENT: '{row.EventType}' does not implement IDomainEvent.";
        }
        @event = JsonSerializer.Deserialize(row.Payload, type) as IDomainEvent;
        return @event is null
            ? $"DESERIALIZE_FAILED: payload for '{row.EventType}' deserialized to null or wrong type."
            : null;
    }

    // The fallback stays on by default because rows written without a type map carry assembly-qualified names,
    // but it lets whoever can write outbox rows pick the event type that is deserialized and dispatched. Say so
    // once, the first time the dispatcher actually relies on it, rather than on every row.
    private void WarnOnFirstAssemblyQualifiedNameFallback(string eventType)
    {
        if (Interlocked.Exchange(ref assemblyQualifiedNameFallbackWarned, 1) != 0)
        {
            return;
        }

        logger?.LogWarning(
            "Outbox event type '{EventType}' was resolved by its assembly-qualified name because " +
            "OutboxTypeMapOptions.AllowAssemblyQualifiedNameFallback is enabled. With the fallback on, anyone who can " +
            "write outbox rows can have any loadable IDomainEvent type deserialized and dispatched. Map every event " +
            "type with UseOutboxTypeMap and set AllowAssemblyQualifiedNameFallback = false once no unmapped rows " +
            "remain. This warning is logged once.",
            eventType);
    }

    private async Task DeadLetterAsync(DbContext db, OutboxMessage row, string reason, CancellationToken cancellationToken)
    {
        // A row that cannot be read cannot become readable without a redeployment, so it is not retried.
        logger?.LogWarning("Outbox row {RowId} dead-lettered: {Reason}", row.Id, reason);
        var deadLetterType = await NotifyRowFailureAsync(
            row, row.RetryCount, isTerminal: true, new InvalidOperationException(reason), cancellationToken).ConfigureAwait(false);

        var deadLettered = new RowState(row.RetryCount, reason, UtcNow());
        if (await TryUpdateRowAsync(db, row, deadLettered, cancellationToken).ConfigureAwait(false))
        {
            OutboxDispatcherDiagnostics.RecordDeadLetter(deadLetterType!);
        }
    }

    // Returns true when the failure dead-lettered the row, i.e. it left the queue.
    private async Task<bool> RecordFailureAsync(OutboxMessage row, Exception exception, string error, CancellationToken cancellationToken)
    {
        OutboxDispatcherDiagnostics.RecordDispatchError(exception.GetType().Name);
        var attempt = row.RetryCount + 1;
        var isTerminal = attempt >= options.MaxRetries;
        var deadLetterType = await NotifyRowFailureAsync(row, attempt, isTerminal, exception, cancellationToken).ConfigureAwait(false);

        // A fresh scope: the row's own DbContext may still hold the handler's writes, or a transaction the
        // handler left open, and none of that may be saved along with the failure.
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DbContext>();
        // Dead-lettering keeps Error and RetryCount for operators; stamping ProcessedOnUtc takes the row out of
        // the unprocessed query.
        var failed = new RowState(attempt, error, isTerminal ? UtcNow() : null);
        if (!await TryUpdateRowAsync(db, row, failed, cancellationToken).ConfigureAwait(false))
        {
            logger?.LogInformation(
                "Outbox row {RowId} failed, but meanwhile it had been replayed, discarded or processed elsewhere; its state was left as found.",
                row.Id);
            return false;
        }
        if (deadLetterType is not null)
        {
            OutboxDispatcherDiagnostics.RecordDeadLetter(deadLetterType);
        }
        return isTerminal;
    }

    /// <summary>
    /// Notifies the row-failure observer and, for a terminal failure, returns the exception type so the caller
    /// can emit the dead_lettered metric AFTER the row's terminal state is persisted. Returns null for a
    /// non-terminal (transient) failure.
    /// </summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "A faulting observer must not stop the dispatcher; it is logged instead.")]
    private async Task<string?> NotifyRowFailureAsync(OutboxMessage msg, int attempt, bool isTerminal, Exception exception, CancellationToken cancellationToken)
    {
        var deadLetterType = isTerminal ? exception.GetType().Name : null;

        var observerRef = rowFailureObserver;
        if (observerRef is null or NullOutboxRowFailureObserver)
        {
            return deadLetterType;
        }
        try
        {
            await observerRef.OnRowFailedAsync(msg.Id, msg.EventType, attempt, isTerminal, exception, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception observerEx) when (!IsShutdown(observerEx, cancellationToken))
        {
            logger?.LogWarning(observerEx,
                "IOutboxRowFailureObserver faulted for row {RowId}; dispatcher continued.", msg.Id);
        }

        return deadLetterType;
    }

    // The handler's writes, if any, and the processed stamp commit together or not at all: when the row was
    // replayed, discarded or finished elsewhere meanwhile, this dispatch does not count, and neither do its writes.
    private static async Task<bool> MarkProcessedAsync(DbContext db, OutboxMessage row, DateTime processedOnUtc, CancellationToken cancellationToken)
    {
        var processed = new RowState(row.RetryCount, Error: null, processedOnUtc);
        if (!db.ChangeTracker.HasChanges() || !db.Database.IsRelational())
        {
            return await TryUpdateRowAsync(db, row, processed, cancellationToken).ConfigureAwait(false);
        }

        // Inside the execution strategy so a retrying strategy (EnableRetryOnFailure) accepts the transaction;
        // changes are accepted only after the commit, so a retried attempt saves them again.
        var marked = await db.Database.CreateExecutionStrategy().ExecuteAsync(
            async token =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync(token).ConfigureAwait(false);
                await db.SaveChangesAsync(acceptAllChangesOnSuccess: false, token).ConfigureAwait(false);
                if (!await TryUpdateRowAsync(db, row, processed, token).ConfigureAwait(false))
                {
                    await transaction.RollbackAsync(token).ConfigureAwait(false);
                    return false;
                }
                await transaction.CommitAsync(token).ConfigureAwait(false);
                return true;
            },
            cancellationToken).ConfigureAwait(false);
        if (marked)
        {
            db.ChangeTracker.AcceptAllChanges();
        }
        return marked;
    }

    // Writes the row's next state only if the row is still in the state it was read in (unprocessed, same
    // RetryCount and Error), so a dashboard replay or discard, or another replica's update, made meanwhile wins.
    // ponytail: without a version column a replay that restores exactly the state read (a first attempt's
    // RetryCount 0 and no error) is indistinguishable from no change; add a rowversion in a major release.
    private static async Task<bool> TryUpdateRowAsync(DbContext db, OutboxMessage row, RowState next, CancellationToken cancellationToken)
    {
        var id = row.Id;
        var readRetryCount = row.RetryCount;
        var readError = row.Error;
        var query = db.Set<OutboxMessage>().Where(m =>
            m.Id == id && m.ProcessedOnUtc == null && m.RetryCount == readRetryCount && m.Error == readError);

        if (db.Database.IsRelational())
        {
            return await query.ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(m => m.RetryCount, next.RetryCount)
                    .SetProperty(m => m.Error, next.Error)
                    .SetProperty(m => m.ProcessedOnUtc, next.ProcessedOnUtc),
                cancellationToken).ConfigureAwait(false) > 0;
        }

        // ponytail: non-relational providers (EF InMemory) have no conditional UPDATE, so the guard is a read
        // followed by a tracked write that a concurrent writer could slip between; they are test doubles.
        var tracked = await query.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (tracked is null)
        {
            return false;
        }
        tracked.RetryCount = next.RetryCount;
        tracked.Error = next.Error;
        tracked.ProcessedOnUtc = next.ProcessedOnUtc;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static Activity? StartDispatchActivity(OutboxMessage row)
        => !string.IsNullOrEmpty(row.TraceParent) && ActivityContext.TryParse(row.TraceParent, row.TraceState, out var parent)
            ? OutboxActivitySource.StartActivity("Outbox.Dispatch", ActivityKind.Consumer, parent)
            : null;

    private static bool IsShutdown(Exception exception, CancellationToken cancellationToken)
        => exception is OperationCanceledException && cancellationToken.IsCancellationRequested;

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;

    private readonly record struct RowState(int RetryCount, string? Error, DateTime? ProcessedOnUtc);
}
