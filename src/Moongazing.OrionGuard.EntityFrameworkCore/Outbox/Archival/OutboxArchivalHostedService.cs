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
    private readonly IOutboxArchiver archiver;
    private readonly ILogger<OutboxArchivalHostedService>? logger;
    // v6.5.14: optional liveness mirror consumed by OutboxArchivalHealthCheck.
    private readonly OutboxArchivalState? state;

    /// <summary>Initializes a new archival worker.</summary>
    /// <param name="options">Archival configuration.</param>
    /// <param name="scopeFactory">Factory used to create per-batch DI scopes for resolving <see cref="DbContext"/>.</param>
    /// <param name="distributedLock">
    /// Distributed lock used to coordinate archival across instances. Use
    /// <see cref="NullDistributedLock"/> for single-instance deployments.
    /// </param>
    /// <param name="archiver">
    /// Pluggable archival strategy. When <see langword="null"/>, defaults to
    /// <see cref="DeleteOutboxArchiver"/> (drop-in equivalent to the pre-v6.5.6 behaviour).
    /// Consumers register <see cref="CopyToTableOutboxArchiver{TArchiveRow}"/> or their own
    /// implementation for copy-to-archive-table / push-to-object-storage flows.
    /// </param>
    /// <param name="logger">Optional logger.</param>
    public OutboxArchivalHostedService(
        OutboxArchivalOptions options,
        IServiceScopeFactory scopeFactory,
        IDistributedLock distributedLock,
        ILogger<OutboxArchivalHostedService>? logger,
        IOutboxArchiver? archiver)
        : this(options, scopeFactory, distributedLock, logger, archiver, state: null)
    {
    }

    /// <summary>v6.5.14 6-arg overload that wires the optional <see cref="OutboxArchivalState"/> mirror.</summary>
    public OutboxArchivalHostedService(
        OutboxArchivalOptions options,
        IServiceScopeFactory scopeFactory,
        IDistributedLock distributedLock,
        ILogger<OutboxArchivalHostedService>? logger,
        IOutboxArchiver? archiver,
        OutboxArchivalState? state)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        this.distributedLock = distributedLock ?? throw new ArgumentNullException(nameof(distributedLock));
        this.archiver = archiver ?? new DeleteOutboxArchiver();
        this.logger = logger;
        this.state = state;
    }

    /// <summary>
    /// Source-compatible 4-arg constructor matching the pre-v6.5.6 ABI. Existing
    /// applications compiled against v6.5.5 directly instantiate this signature; the v6.5.6
    /// 5-arg ctor would otherwise be a binary break that surfaces as MissingMethodException
    /// at runtime. Defaults the archiver to <see cref="DeleteOutboxArchiver"/>.
    /// </summary>
    public OutboxArchivalHostedService(
        OutboxArchivalOptions options,
        IServiceScopeFactory scopeFactory,
        IDistributedLock distributedLock,
        ILogger<OutboxArchivalHostedService>? logger = null)
        : this(options, scopeFactory, distributedLock, logger, archiver: null)
    {
    }

    /// <summary>Archives one batch of processed rows older than the retention cutoff. Public for tests.</summary>
    /// <param name="cancellationToken">Token used to observe cancellation requests.</param>
    /// <returns>The number of rows archived in this batch.</returns>
    public async Task<int> ArchiveBatchAsync(CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow - options.RetentionPeriod;
        await using var scope = scopeFactory.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<DbContext>();

        // v6.5.21: time the full archival round-trip including the scope/context
        // resolution above. Records on EVERY cycle (zero-row included) so a slow sink
        // surfaces even when the backlog is empty.
        var cycleSw = System.Diagnostics.Stopwatch.StartNew();
        var archived = await archiver.ArchiveAsync(ctx, cutoff, options, cancellationToken).ConfigureAwait(false);
        cycleSw.Stop();
        OutboxArchivalDiagnostics.RecordArchiveCycleDuration(cycleSw.Elapsed.TotalMilliseconds);

        if (archived > 0)
        {
            logger?.LogInformation(
                "Outbox archival processed {Count} rows older than {Cutoff:O}.", archived, cutoff);
            // v6.5.20: record batch size on non-empty cycles only so the histogram
            // tail reflects produced batches, not idle polls. archived == 0 cycles
            // are tracked by the v6.5.14 liveness gauge.
            OutboxArchivalDiagnostics.RecordArchiveBatchSize(archived);
        }
        // v6.5.14: record liveness regardless of whether rows were archived. A successful
        // call with archived == 0 still proves the worker reached the backend - exactly
        // what the OutboxArchivalHealthCheck needs to distinguish "stuck" from "idle".
        state?.RecordSuccessfulBatch(DateTime.UtcNow);

        return archived;
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
