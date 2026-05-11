using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moongazing.OrionGuard.Domain.Events;

namespace Moongazing.OrionGuard.EntityFrameworkCore.Outbox;

/// <summary>
/// Polls <see cref="OutboxMessage"/> rows from the consumer's <see cref="DbContext"/>, deserializes
/// each event by its <see cref="OutboxMessage.EventType"/>, and dispatches via
/// <see cref="IDomainEventDispatcher"/>. On dispatch failure, increments <see cref="OutboxMessage.RetryCount"/>;
/// after <see cref="OutboxOptions.MaxRetries"/> attempts the row is dead-lettered (marked processed).
/// </summary>
public sealed class OutboxDispatcherHostedService : BackgroundService
{
    private static readonly ActivitySource OutboxActivitySource = new("Moongazing.OrionGuard.DomainEvents", "6.3.0");

    private readonly OutboxOptions options;
    private readonly IServiceScopeFactory scopeFactory;

    /// <summary>Initializes a new worker.</summary>
    /// <param name="options">Outbox dispatch configuration.</param>
    /// <param name="scopeFactory">Factory used to create per-batch DI scopes for resolving <see cref="DbContext"/> and <see cref="IDomainEventDispatcher"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null"/>.</exception>
    public OutboxDispatcherHostedService(OutboxOptions options, IServiceScopeFactory scopeFactory)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
    }

    /// <inheritdoc />
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Per-batch faults are intentionally swallowed; per-row faults are already recorded on the OutboxMessage row.")]
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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
                // swallow per-batch fault; per-row faults already recorded on the row
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
                var type = Type.GetType(msg.EventType)
                    ?? throw new InvalidOperationException($"Cannot resolve event type '{msg.EventType}'.");
                var @event = (IDomainEvent)JsonSerializer.Deserialize(msg.Payload, type)!;
                await dispatcher.DispatchAsync(@event, cancellationToken).ConfigureAwait(false);
                msg.ProcessedOnUtc = DateTime.UtcNow;
                msg.Error = null;
            }
            catch (Exception ex)
            {
                msg.RetryCount++;
                msg.Error = ex.ToString();
                if (msg.RetryCount >= options.MaxRetries)
                {
                    msg.ProcessedOnUtc = DateTime.UtcNow;   // dead-letter
                }
            }
            finally
            {
                activity?.Dispose();
            }
        }

        await ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
