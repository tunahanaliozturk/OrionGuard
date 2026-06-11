namespace Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Archival;

using System.Collections.Generic;

/// <summary>
/// <see cref="IOutboxArchiveSink"/> that forwards every <see cref="WriteAsync"/> to a
/// configured fan-out of inner sinks. Pairs with deployments that want archival
/// redundancy - e.g. write to both S3 (long-term cold storage) AND a local rotating-file
/// sink (operator-readable shadow). Failure semantics are configurable:
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><description><see cref="CompositeOutboxArchiveSinkMode.FailFast"/> (default): the first sink that throws aborts the composite write and bubbles the exception. Remaining sinks are NOT called. Use when consistency across sinks matters more than coverage.</description></item>
///   <item><description><see cref="CompositeOutboxArchiveSinkMode.BestEffort"/>: every sink is called; any exceptions are aggregated and surfaced as an <see cref="AggregateException"/> ONLY if EVERY sink threw. If at least one sink succeeded the call returns successfully. Use when you want the archive to land in at least one destination but cannot afford a fan-out failure to abort the sweep.</description></item>
/// </list>
/// <para>
/// Sinks are invoked SEQUENTIALLY in registration order. Parallel fan-out is intentionally
/// not built in - cloud-object-store clients typically have their own connection-pool
/// constraints that benefit from serial calls more than from fan-out concurrency. A
/// future <c>ParallelCompositeOutboxArchiveSink</c> can ship if measurements justify it.
/// </para>
/// </remarks>
public sealed class CompositeOutboxArchiveSink : IOutboxArchiveSink
{
    private readonly IReadOnlyList<IOutboxArchiveSink> sinks;
    private readonly CompositeOutboxArchiveSinkMode mode;

    /// <summary>Construct with the inner sinks and the failure-mode policy.</summary>
    public CompositeOutboxArchiveSink(
        IEnumerable<IOutboxArchiveSink> sinks,
        CompositeOutboxArchiveSinkMode mode = CompositeOutboxArchiveSinkMode.FailFast)
    {
        ArgumentNullException.ThrowIfNull(sinks);
        var snapshot = sinks.ToArray();
        if (snapshot.Length == 0)
        {
            throw new ArgumentException(
                "CompositeOutboxArchiveSink requires at least one inner sink.",
                nameof(sinks));
        }
        if (snapshot.Any(s => s is null))
        {
            throw new ArgumentException(
                "CompositeOutboxArchiveSink: every inner sink must be non-null.",
                nameof(sinks));
        }
        this.sinks = snapshot;
        this.mode = mode;
    }

    /// <inheritdoc />
    public async Task WriteAsync(string keyHint, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(keyHint);
        if (mode == CompositeOutboxArchiveSinkMode.FailFast)
        {
            foreach (var sink in sinks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await sink.WriteAsync(keyHint, payload, cancellationToken).ConfigureAwait(false);
            }
            return;
        }

        // BestEffort: collect failures; return success when at least one sink succeeded.
        List<Exception>? failures = null;
        var anySuccess = false;
        foreach (var sink in sinks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await sink.WriteAsync(keyHint, payload, cancellationToken).ConfigureAwait(false);
                anySuccess = true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Caller cancellation - propagate immediately without aggregating.
                throw;
            }
#pragma warning disable CA1031 // composite captures by design; aggregation decision is at the bottom
            catch (Exception ex)
#pragma warning restore CA1031
            {
                (failures ??= new List<Exception>()).Add(ex);
            }
        }
        if (!anySuccess && failures is not null)
        {
            throw new AggregateException(
                $"CompositeOutboxArchiveSink (BestEffort): all {sinks.Count} sinks failed.",
                failures);
        }
    }
}

/// <summary>Failure policy for <see cref="CompositeOutboxArchiveSink"/>.</summary>
public enum CompositeOutboxArchiveSinkMode
{
    /// <summary>First-failure aborts; remaining sinks are not called.</summary>
    FailFast = 0,

    /// <summary>Every sink is called; aggregated failure thrown ONLY if every sink threw.</summary>
    BestEffort = 1,
}
