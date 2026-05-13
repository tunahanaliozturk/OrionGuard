using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moongazing.OrionGuard.Domain.Events;

namespace Moongazing.OrionGuard.EntityFrameworkCore.Outbox;

/// <summary>
/// Polls <see cref="OutboxMessage"/> rows from the consumer's <see cref="DbContext"/>, deserializes
/// each event by its <see cref="OutboxMessage.EventType"/>, and dispatches via
/// <see cref="IDomainEventDispatcher"/>. On dispatch failure, increments <see cref="OutboxMessage.RetryCount"/>;
/// after <see cref="OutboxOptions.MaxRetries"/> attempts the row is dead-lettered (marked processed).
/// </summary>
/// <remarks>
/// IMPORTANT: This v6.3.0 implementation assumes a single instance per database.
/// Concurrent instances will double-dispatch events because there is no row-level
/// locking. If you scale horizontally (e.g. Kubernetes with replicas &gt; 1),
/// either pin the worker to one replica via a leader-election mechanism, or run
/// it in a dedicated singleton service. Distributed locking lands in v6.4.
/// </remarks>
public sealed class OutboxDispatcherHostedService : BackgroundService
{
    private static readonly ActivitySource OutboxActivitySource = new("Moongazing.OrionGuard.DomainEvents", "6.3.0");

    private readonly OutboxOptions options;
    private readonly IServiceScopeFactory scopeFactory;
    private readonly ILogger<OutboxDispatcherHostedService>? logger;

    /// <summary>Initializes a new worker.</summary>
    /// <param name="options">Outbox dispatch configuration.</param>
    /// <param name="scopeFactory">Factory used to create per-batch DI scopes for resolving <see cref="DbContext"/> and <see cref="IDomainEventDispatcher"/>.</param>
    /// <param name="logger">Optional logger used to surface a startup warning about the single-instance assumption.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> or <paramref name="scopeFactory"/> is <see langword="null"/>.</exception>
    public OutboxDispatcherHostedService(
        OutboxOptions options,
        IServiceScopeFactory scopeFactory,
        ILogger<OutboxDispatcherHostedService>? logger = null)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        this.logger = logger;
    }

    /// <inheritdoc />
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Per-batch faults are intentionally swallowed; per-row faults are already recorded on the OutboxMessage row.")]
    [RequiresUnreferencedCode("Type.GetType deserializes events by assembly-qualified name; event types must be preserved in the AOT build (e.g. via DynamicDependency or by rooting them in your application). System.Text.Json source generation is recommended for full AOT support.")]
    [RequiresDynamicCode("JsonSerializer.Deserialize(string, Type) requires runtime code generation under AOT. Use System.Text.Json source generation for full AOT support.")]
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger?.LogWarning(
            "OrionGuard outbox dispatcher started. NOTE: this v6.3.0 implementation assumes a SINGLE instance per database. " +
            "Running multiple instances concurrently will double-dispatch every event. " +
            "Distributed locking lands in v6.4.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
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
                await Task.Delay(options.PollingInterval, stoppingToken).ConfigureAwait(false);
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
            return;
        }

        foreach (var msg in batch)
        {
            Activity? activity = null;
            if (!string.IsNullOrEmpty(msg.TraceParent)
                && ActivityContext.TryParse(msg.TraceParent, msg.TraceState, out var parentContext))
            {
                activity = OutboxActivitySource.StartActivity("Outbox.Dispatch", ActivityKind.Consumer, parentContext);
            }
            try
            {
                var type = Type.GetType(msg.EventType);
                if (type is null)
                {
                    // Why: an unresolvable type cannot become resolvable without a redeployment,
                    // so retrying is pointless. Dead-letter immediately with a clear error marker.
                    msg.Error = $"TYPE_NOT_FOUND: cannot resolve event type '{msg.EventType}'. " +
                                "Type was renamed, moved, or its assembly is not loaded.";
                    msg.ProcessedOnUtc = DateTime.UtcNow;
                    logger?.LogWarning(
                        "Outbox row {RowId} dead-lettered: type '{EventType}' could not be resolved.",
                        msg.Id, msg.EventType);
                }
                else if (!typeof(IDomainEvent).IsAssignableFrom(type))
                {
                    msg.Error = $"TYPE_NOT_DOMAIN_EVENT: '{msg.EventType}' does not implement IDomainEvent.";
                    msg.ProcessedOnUtc = DateTime.UtcNow;
                    logger?.LogWarning(
                        "Outbox row {RowId} dead-lettered: type '{EventType}' is not IDomainEvent.",
                        msg.Id, msg.EventType);
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
                    }
                    else
                    {
                        await dispatcher.DispatchAsync(@event, cancellationToken).ConfigureAwait(false);
                        msg.ProcessedOnUtc = DateTime.UtcNow;
                        msg.Error = null;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                msg.RetryCount++;
                msg.Error = ex.ToString();
                if (msg.RetryCount >= options.MaxRetries)
                {
                    // Why: stamping ProcessedOnUtc removes the row from the unprocessed-rows query
                    // while preserving Error/RetryCount for operators to inspect (dead-letter).
                    msg.ProcessedOnUtc = DateTime.UtcNow;
                }
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
