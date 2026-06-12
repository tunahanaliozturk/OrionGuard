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
/// Multi-instance safety: at startup the worker resolves an <see cref="IDistributedLock"/>
/// (default <see cref="SkipLockedDistributedLock"/>) and acquires <see cref="OutboxOptions.LockKey"/>
/// before each batch. Instances that fail to acquire the lock sleep and retry, so only one replica
/// dispatches at a time. Pin to <see cref="NullDistributedLock"/> for single-instance deployments
/// that do not want to apply the <c>OrionGuard_OutboxLocks</c> migration.
/// </remarks>
public sealed class OutboxDispatcherHostedService : BackgroundService
{
    private static readonly ActivitySource OutboxActivitySource = new("Moongazing.OrionGuard.DomainEvents", "6.4.0");

    private readonly OutboxOptions options;
    private readonly IServiceScopeFactory scopeFactory;
    private readonly IDistributedLock distributedLock;
    private readonly OutboxTypeMapRegistry typeMap;
    private readonly OutboxTypeMapOptions typeMapOptions;
    private readonly IOutboxWakeSignal wakeSignal;
    private readonly ILogger<OutboxDispatcherHostedService>? logger;
    // v6.5.23 optional consumer-registered failure observer. Null and
    // NullOutboxRowFailureObserver are both treated as 'no observer' so the dispatcher
    // skips the call entirely.
    private readonly IOutboxRowFailureObserver? rowFailureObserver;

    /// <summary>Initializes a new worker.</summary>
    /// <param name="options">Outbox dispatch configuration.</param>
    /// <param name="scopeFactory">Factory used to create per-batch DI scopes for resolving <see cref="DbContext"/> and <see cref="IDomainEventDispatcher"/>.</param>
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
    /// When <see langword="null"/>, defaults to <see cref="NullOutboxWakeSignal"/> (polling-only,
    /// behaviour identical to v6.4 / v6.5.0).
    /// </param>
    /// <param name="logger">Optional logger used to surface startup and per-row diagnostic messages.</param>
    /// <param name="rowFailureObserver">
    /// v6.5.23 optional consumer-registered observer notified for EVERY swallowed per-row
    /// failure (transient + terminal). Defaults to no-op when null or
    /// <see cref="NullOutboxRowFailureObserver"/> is supplied.
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
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        this.distributedLock = distributedLock ?? new NullDistributedLock();
        this.typeMap = typeMap ?? new OutboxTypeMapRegistry();
        this.typeMapOptions = typeMapOptions ?? new OutboxTypeMapOptions();
        this.logger = logger;
        this.wakeSignal = wakeSignal ?? new NullOutboxWakeSignal();
        this.rowFailureObserver = rowFailureObserver is NullOutboxRowFailureObserver ? null : rowFailureObserver;
    }

    // v6.5.23 fix (codex P2 + coderabbit Major): centralised observer notification so
    // every terminal/transient failure path calls the observer. OperationCanceledException
    // unconditionally propagates so cancellation is never downgraded to a warning.
    private async Task NotifyRowFailureAsync(OutboxMessage msg, int attempt, bool isTerminal, Exception exception, CancellationToken cancellationToken)
    {
        var observerRef = rowFailureObserver;
        if (observerRef is null or NullOutboxRowFailureObserver)
        {
            return;
        }
        try
        {
            await observerRef.OnRowFailedAsync(msg.Id, msg.EventType, attempt, isTerminal, exception, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031
        catch (Exception observerEx)
#pragma warning restore CA1031
        {
            logger?.LogWarning(observerEx,
                "IOutboxRowFailureObserver faulted for row {RowId}; dispatcher continued.", msg.Id);
        }
    }

    /// <inheritdoc />
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Per-batch faults are intentionally swallowed; per-row faults are already recorded on the OutboxMessage row.")]
    [RequiresUnreferencedCode("Type.GetType deserializes events by assembly-qualified name; event types must be preserved in the AOT build (e.g. via DynamicDependency or by rooting them in your application). System.Text.Json source generation is recommended for full AOT support.")]
    [RequiresDynamicCode("JsonSerializer.Deserialize(string, Type) requires runtime code generation under AOT. Use System.Text.Json source generation for full AOT support.")]
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger?.LogInformation(
            "OrionGuard outbox dispatcher started with distributed locking key '{LockKey}' (lease {Lease}).",
            options.LockKey, options.LockLeaseDuration);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var handle = await distributedLock.TryAcquireAsync(
                    options.LockKey,
                    options.LockLeaseDuration,
                    stoppingToken).ConfigureAwait(false);

                if (handle is null)
                {
                    // Why: another instance holds the lease. Sleep and retry — do not dispatch.
                    await wakeSignal.WaitForNextTickAsync(options.PollingInterval, stoppingToken).ConfigureAwait(false);
                    continue;
                }

                await ProcessBatchAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Why: swallow per-batch faults so the worker survives transient infrastructure
                // errors. Per-row dispatch faults are recorded on the OutboxMessage row itself
                // (Error/RetryCount), so they are not lost.
            }

            try
            {
                await wakeSignal.WaitForNextTickAsync(options.PollingInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>Processes one batch of unprocessed outbox rows. Public for tests.</summary>
    /// <param name="cancellationToken">Token used to observe cancellation requests.</param>
    /// <returns>A task that completes when the batch is processed and saved.</returns>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Per-row dispatch faults are recorded on the OutboxMessage row and used to drive retry / dead-letter policy.")]
    [RequiresUnreferencedCode("Type.GetType deserializes events by assembly-qualified name; event types must be preserved in the AOT build (e.g. via DynamicDependency or by rooting them in your application). System.Text.Json source generation is recommended for full AOT support.")]
    [RequiresDynamicCode("JsonSerializer.Deserialize(string, Type) requires runtime code generation under AOT. Use System.Text.Json source generation for full AOT support.")]
    public async Task ProcessBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<DbContext>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDomainEventDispatcher>();

        var batch = await ctx.Set<OutboxMessage>()
            .Where(m => m.ProcessedOnUtc == null)
            .OrderBy(m => m.OccurredOnUtc)
            .Take(options.BatchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (batch.Count == 0)
        {
            // v6.5.17 idle-poll counter: lets operators see what fraction of the
            // dispatcher's wakeups produced nothing and tune PollingInterval / BatchSize.
            OutboxDispatcherDiagnostics.RecordIdlePoll();
            return;
        }

        foreach (var msg in batch)
        {
            // v6.5.16: per-row queue lag captured during the success path and emitted
            // after SaveChangesAsync confirms persistence (avoids double-count on
            // re-dispatch).
            double? pendingQueueLagMs = null;
            // v6.5.19: stash payload size and emit AFTER SaveChangesAsync confirms the
            // row is persisted, matching v6.5.16's queue_lag pattern. Recording before
            // persistence would double-count on a SaveChanges failure that re-dispatches
            // the row on the next cycle.
            int? pendingRowPayloadBytes = null;
            Activity? activity = null;
            if (!string.IsNullOrEmpty(msg.TraceParent)
                && ActivityContext.TryParse(msg.TraceParent, msg.TraceState, out var parentContext))
            {
                activity = OutboxActivitySource.StartActivity("Outbox.Dispatch", ActivityKind.Consumer, parentContext);
            }
            try
            {
                // Resolution: registry first, AQN fallback if enabled. Why: the registry decouples
                // persisted payloads from internal type identity, while AQN preserves v6.3 source
                // compatibility for consumers who have not yet adopted logical names.
                Type? type;
                if (typeMap.TryResolve(msg.EventType, out var resolved))
                {
                    type = resolved;
                }
                else if (typeMapOptions.AllowAssemblyQualifiedNameFallback)
                {
                    type = Type.GetType(msg.EventType);
                }
                else
                {
                    type = null;
                }

                if (type is null)
                {
                    // Why: an unresolvable type cannot become resolvable without a redeployment,
                    // so retrying is pointless. Dead-letter immediately with a clear error marker
                    // that records the current fallback state for operators.
                    msg.Error = $"TYPE_NOT_FOUND: cannot resolve event type '{msg.EventType}'. " +
                                $"Registry has no mapping and AQN fallback is " +
                                $"{(typeMapOptions.AllowAssemblyQualifiedNameFallback ? "enabled but resolution failed" : "disabled")}.";
                    msg.ProcessedOnUtc = DateTime.UtcNow;
                    logger?.LogWarning(
                        "Outbox row {RowId} dead-lettered: type '{EventType}' could not be resolved.",
                        msg.Id, msg.EventType);
                    // v6.5.23 fix (codex P2): notify observer on validation dead-letter
                    // paths too, not just the catch block. Synthesise an exception so
                    // the contract surface stays uniform across all terminal paths.
                    await NotifyRowFailureAsync(msg, msg.RetryCount, isTerminal: true,
                        new InvalidOperationException(msg.Error), cancellationToken).ConfigureAwait(false);
                }
                else if (!typeof(IDomainEvent).IsAssignableFrom(type))
                {
                    msg.Error = $"TYPE_NOT_DOMAIN_EVENT: '{msg.EventType}' does not implement IDomainEvent.";
                    msg.ProcessedOnUtc = DateTime.UtcNow;
                    logger?.LogWarning(
                        "Outbox row {RowId} dead-lettered: type '{EventType}' is not IDomainEvent.",
                        msg.Id, msg.EventType);
                    await NotifyRowFailureAsync(msg, msg.RetryCount, isTerminal: true,
                        new InvalidOperationException(msg.Error), cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    var deserialized = JsonSerializer.Deserialize(msg.Payload, type);
                    if (deserialized is not IDomainEvent @event)
                    {
                        msg.Error = $"DESERIALIZE_FAILED: payload for '{msg.EventType}' deserialized to null or wrong type.";
                        msg.ProcessedOnUtc = DateTime.UtcNow;
                        logger?.LogWarning(
                            "Outbox row {RowId} dead-lettered: payload deserialized to null or wrong type.",
                            msg.Id);
                        await NotifyRowFailureAsync(msg, msg.RetryCount, isTerminal: true,
                            new InvalidOperationException(msg.Error), cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        await dispatcher.DispatchAsync(@event, cancellationToken).ConfigureAwait(false);
                        var processedUtc = DateTime.UtcNow;
                        msg.ProcessedOnUtc = processedUtc;
                        msg.Error = null;
                        // v6.5.16: stash the lag so it can be recorded AFTER SaveChangesAsync
                        // confirms the row is persisted. Recording before persistence would
                        // double-count when SaveChanges fails and the row is re-dispatched on
                        // the next poll.
                        pendingQueueLagMs = (processedUtc - msg.OccurredOnUtc).TotalMilliseconds;
                        // v6.5.19: stash for post-SaveChanges emit so a SaveChanges
                        // failure does NOT cause a double count on re-dispatch.
                        pendingRowPayloadBytes = msg.Payload?.Length;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // v6.5.18: emit the dispatch error counter for EVERY swallowed failure
                // (transient + terminal). Pairs with the dead-letter path (which only
                // fires when MaxRetries is reached) so operators see the upstream
                // pressure leading to a dead-letter.
                OutboxDispatcherDiagnostics.RecordDispatchError(ex.GetType().Name);
                msg.RetryCount++;
                msg.Error = ex.ToString();
                var isTerminal = msg.RetryCount >= options.MaxRetries;
                if (isTerminal)
                {
                    // Why: stamping ProcessedOnUtc removes the row from the unprocessed-rows query
                    // while preserving Error/RetryCount for operators to inspect (dead-letter).
                    msg.ProcessedOnUtc = DateTime.UtcNow;
                }
                await NotifyRowFailureAsync(msg, msg.RetryCount, isTerminal, ex, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                activity?.Dispose();
            }

            // Why: persist this row's state per iteration (not per batch) so a SaveChanges failure
            // mid-batch does not lose state for rows that already dispatched. A lost save means the
            // row is re-picked on the next poll, which idempotent handlers will tolerate as a duplicate.
            try
            {
                await ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                if (pendingQueueLagMs is { } lag)
                {
                    OutboxDispatcherDiagnostics.RecordQueueLag(lag);
                    pendingQueueLagMs = null;
                }
                if (pendingRowPayloadBytes is { } bytes)
                {
                    OutboxDispatcherDiagnostics.RecordRowPayloadSize(bytes);
                    pendingRowPayloadBytes = null;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception saveEx)
            {
                msg.Error = $"Row dispatched but state update failed: {saveEx.GetType().Name}: {saveEx.Message}";
                throw;
            }
        }
    }
}
