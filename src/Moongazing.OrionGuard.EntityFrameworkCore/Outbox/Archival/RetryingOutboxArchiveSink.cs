namespace Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Archival;

/// <summary>
/// <see cref="IOutboxArchiveSink"/> decorator that retries the inner sink's
/// <see cref="IOutboxArchiveSink.WriteAsync"/> on transient failures with jittered
/// exponential backoff. Pairs with cloud-object-store sinks (S3, Azure Blob, GCS) where
/// transient socket failures are common; instead of failing the archival sweep on the
/// first 503 the decorator retries up to <see cref="RetryingOutboxArchiveSinkOptions.MaxAttempts"/>
/// times before giving up.
/// </summary>
/// <remarks>
/// Retries apply to ALL exceptions by default; consumers wanting to fail-fast on
/// non-transient classes (e.g. <see cref="ArgumentException"/>) supply a
/// <see cref="RetryingOutboxArchiveSinkOptions.IsRetryable"/> predicate. The decorator
/// honours <see cref="OperationCanceledException"/>: cancellation propagates immediately
/// without consuming a retry slot.
/// </remarks>
public sealed class RetryingOutboxArchiveSink : IOutboxArchiveSink
{
    private readonly IOutboxArchiveSink inner;
    private readonly RetryingOutboxArchiveSinkOptions options;

    /// <summary>Construct over an inner sink and the retry options.</summary>
    public RetryingOutboxArchiveSink(IOutboxArchiveSink inner, RetryingOutboxArchiveSinkOptions options)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(options);
        options.ValidateAndNormalise();
        this.inner = inner;
        this.options = options;
    }

    /// <inheritdoc />
    public async Task WriteAsync(string keyHint, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(keyHint);
        Exception? lastFailure = null;
        var rng = options.RandomFactory();
        for (var attempt = 1; attempt <= options.MaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await inner.WriteAsync(keyHint, payload, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // CALLER cancellation. Propagate without consuming a retry slot. We gate
                // on the caller's token because inner sinks built on HttpClient can
                // surface request timeouts as TaskCanceledException even when the
                // caller's token never fired - those are transient and SHOULD go through
                // the IsRetryable predicate, not bypass the retry loop.
                throw;
            }
#pragma warning disable CA1031 // decorator catches by design - the IsRetryable predicate triages
            catch (Exception ex)
#pragma warning restore CA1031
            {
                lastFailure = ex;
                if (attempt == options.MaxAttempts || !options.IsRetryable(ex))
                {
                    throw;
                }
                var delay = ComputeBackoff(attempt, rng);
                try
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException oce)
                {
                    // Cancelled while waiting to retry - surface the last failure as the
                    // InnerException so the caller sees the underlying problem rather
                    // than a synthetic TaskCanceledException with no context.
                    throw new OperationCanceledException(
                        "RetryingOutboxArchiveSink cancelled while waiting to retry.",
                        innerException: lastFailure ?? oce,
                        token: oce.CancellationToken);
                }
            }
        }
        // Should be unreachable - the final attempt's failure throws above.
        throw new InvalidOperationException(
            "RetryingOutboxArchiveSink: retry loop exited without success or rethrow.",
            lastFailure);
    }

    private TimeSpan ComputeBackoff(int attempt, Random rng)
    {
        // Exponential: BaseDelay * 2^(attempt-1), capped at MaxDelay. Apply random jitter
        // in [0.5 * computed, 1.0 * computed] so concurrent waiters do not retry in lockstep.
        var ceiling = options.BaseDelay.TotalMilliseconds * Math.Pow(2, Math.Min(attempt - 1, 30));
        var capped = Math.Min(ceiling, options.MaxDelay.TotalMilliseconds);
        var floor = capped * 0.5;
        var jittered = floor + rng.NextDouble() * (capped - floor);
        return TimeSpan.FromMilliseconds(jittered);
    }
}

/// <summary>Configuration for <see cref="RetryingOutboxArchiveSink"/>.</summary>
public sealed class RetryingOutboxArchiveSinkOptions
{
    /// <summary>Static default instance: 5 attempts, 100 ms base, 5 s cap.</summary>
    public static RetryingOutboxArchiveSinkOptions Default { get; } = new();

    /// <summary>Maximum number of attempts including the first try. Default 5.</summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>Initial backoff delay. Default 100 ms.</summary>
    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>Upper bound on per-attempt backoff. Default 5 seconds.</summary>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Predicate that decides whether an exception type is retryable. Default returns
    /// true for everything except <see cref="OperationCanceledException"/> (which is
    /// already handled at the call site). Override to restrict retries to specific
    /// transient classes (e.g. <c>S3.AmazonS3Exception</c> with code 503).
    /// </summary>
    public Func<Exception, bool> IsRetryable { get; set; } = _ => true;

    /// <summary>Factory for the random generator used to pick jitter.</summary>
    public Func<Random> RandomFactory { get; set; } = () => Random.Shared;

    internal void ValidateAndNormalise()
    {
        if (MaxAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxAttempts), MaxAttempts,
                "RetryingOutboxArchiveSinkOptions.MaxAttempts must be at least 1.");
        }
        if (BaseDelay <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(BaseDelay), BaseDelay,
                "RetryingOutboxArchiveSinkOptions.BaseDelay must be positive.");
        }
        if (MaxDelay < BaseDelay)
        {
            throw new ArgumentException(
                $"RetryingOutboxArchiveSinkOptions: MaxDelay ({MaxDelay}) must be >= BaseDelay ({BaseDelay}).");
        }
        // Configuration binding (IConfiguration.Bind / Options.Configure with null
        // explicit assignments) can leave these delegates null even when callers pass an
        // options object that nominally satisfies the type. Surface that as a fast
        // construction failure so the first WriteAsync call does not blow up with a
        // NullReferenceException.
        if (IsRetryable is null)
        {
            throw new ArgumentNullException(
                nameof(IsRetryable),
                "RetryingOutboxArchiveSinkOptions.IsRetryable must not be null. Default retries everything.");
        }
        if (RandomFactory is null)
        {
            throw new ArgumentNullException(
                nameof(RandomFactory),
                "RetryingOutboxArchiveSinkOptions.RandomFactory must not be null. Default returns Random.Shared.");
        }
    }
}
