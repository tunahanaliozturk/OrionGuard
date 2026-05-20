using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Locking;

namespace Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Archival;

/// <summary>
/// Periodically deletes processed outbox rows older than <see cref="OutboxArchivalOptions.RetentionPeriod"/>.
/// Opt-in: register via <c>opts.UseOutboxArchival(...)</c>. Coordinates with the dispatcher through a
/// separate <see cref="IDistributedLock"/> key (default <c>orion_guard_outbox_archival</c>).
/// </summary>
public sealed class OutboxArchivalHostedService : BackgroundService
{
    private readonly OutboxArchivalOptions options;
    private readonly IServiceScopeFactory scopeFactory;
    private readonly IDistributedLock distributedLock;
    private readonly ILogger<OutboxArchivalHostedService>? logger;

    /// <summary>Initializes a new archival worker.</summary>
    /// <param name="options">Archival configuration.</param>
    /// <param name="scopeFactory">Factory used to create per-batch DI scopes for resolving <see cref="DbContext"/>.</param>
    /// <param name="distributedLock">
    /// Distributed lock used to coordinate archival across instances. Use
    /// <see cref="NullDistributedLock"/> for single-instance deployments.
    /// </param>
    /// <param name="logger">Optional logger.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="options"/>, <paramref name="scopeFactory"/>, or
    /// <paramref name="distributedLock"/> is <see langword="null"/>.
    /// </exception>
    public OutboxArchivalHostedService(
        OutboxArchivalOptions options,
        IServiceScopeFactory scopeFactory,
        IDistributedLock distributedLock,
        ILogger<OutboxArchivalHostedService>? logger = null)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        this.distributedLock = distributedLock ?? throw new ArgumentNullException(nameof(distributedLock));
        this.logger = logger;
    }

    /// <summary>Deletes one batch of processed rows older than the retention cutoff. Public for tests.</summary>
    /// <param name="cancellationToken">Token used to observe cancellation requests.</param>
    /// <returns>The number of rows deleted in this batch.</returns>
    public async Task<int> ArchiveBatchAsync(CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow - options.RetentionPeriod;
        await using var scope = scopeFactory.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<DbContext>();

        var query = ctx.Set<OutboxMessage>()
            .Where(m => m.ProcessedOnUtc != null && m.ProcessedOnUtc < cutoff);

        if (options.PreserveDeadLetters)
        {
            query = query.Where(m => m.Error == null);
        }

        var deleted = await query
            .OrderBy(m => m.ProcessedOnUtc)
            .Take(options.BatchSize)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        if (deleted > 0)
        {
            logger?.LogInformation(
                "Outbox archival deleted {Count} rows older than {Cutoff:O}.", deleted, cutoff);
        }

        return deleted;
    }

    /// <inheritdoc />
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Per-batch faults are intentionally swallowed so the worker survives transient infrastructure errors.")]
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger?.LogInformation(
            "OrionGuard outbox archival started. Retention {Retention}, batch {BatchSize}, polling {Polling}.",
            options.RetentionPeriod, options.BatchSize, options.PollingInterval);

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
                    // Why: another instance owns the lease. Sleep and retry — do not archive.
                    await Task.Delay(options.PollingInterval, stoppingToken).ConfigureAwait(false);
                    continue;
                }

                await ArchiveBatchAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Outbox archival batch failed.");
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
}
