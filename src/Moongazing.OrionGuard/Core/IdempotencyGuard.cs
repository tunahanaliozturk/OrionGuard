using System.Collections.Concurrent;

namespace Moongazing.OrionGuard.Core;

/// <summary>
/// Guards against duplicate operations using idempotency keys.
/// Tracks operation IDs in a bounded, thread-safe cache with TTL.
/// Use in microservices, payment processing, and event handlers.
/// </summary>
public sealed class IdempotencyGuard
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _processedKeys = new();
    private readonly TimeSpan _ttl;
    private readonly int _maxKeys;
    private readonly object _cleanupLock = new();
    private DateTimeOffset _lastCleanup = DateTimeOffset.UtcNow;

    /// <summary>
    /// Creates an idempotency guard with configurable TTL and capacity.
    /// </summary>
    /// <param name="ttl">How long to remember processed keys. Default: 24 hours.</param>
    /// <param name="maxKeys">Maximum number of keys to track. Default: 100,000.</param>
    public IdempotencyGuard(TimeSpan? ttl = null, int maxKeys = 100_000)
    {
        _ttl = ttl ?? TimeSpan.FromHours(24);
        _maxKeys = maxKeys;
    }

    /// <summary>
    /// Default instance with 24h TTL and 100K key capacity.
    /// </summary>
    public static IdempotencyGuard Default { get; } = new();

    /// <summary>
    /// Throws if this key has already been processed within the TTL window.
    /// Thread-safe.
    /// </summary>
    /// <param name="idempotencyKey">Unique operation identifier</param>
    /// <param name="parameterName">Parameter name for error messages</param>
    public void AgainstDuplicateOperation(string idempotencyKey, string parameterName = "idempotencyKey")
    {
        ArgumentNullException.ThrowIfNull(idempotencyKey);
        CleanupIfNeeded();

        var now = DateTimeOffset.UtcNow;

        if (_processedKeys.TryGetValue(idempotencyKey, out var existingTimestamp))
        {
            if (now - existingTimestamp < _ttl)
                throw new InvalidOperationException($"Duplicate operation detected for {parameterName} '{idempotencyKey}'. This operation was already processed at {existingTimestamp:O}.");

            // Expired — update timestamp
            _processedKeys[idempotencyKey] = now;
            return;
        }

        if (!_processedKeys.TryAdd(idempotencyKey, now))
        {
            // Race condition — another thread added it
            throw new InvalidOperationException($"Duplicate operation detected for {parameterName} '{idempotencyKey}'.");
        }
    }

    /// <summary>
    /// Returns true if the key has NOT been processed (safe to proceed).
    /// Returns false if it's a duplicate. Does NOT throw.
    /// </summary>
    public bool TryProcess(string idempotencyKey)
    {
        ArgumentNullException.ThrowIfNull(idempotencyKey);
        CleanupIfNeeded();

        var now = DateTimeOffset.UtcNow;

        if (_processedKeys.TryGetValue(idempotencyKey, out var existingTimestamp) && now - existingTimestamp < _ttl)
            return false;

        _processedKeys[idempotencyKey] = now;
        return true;
    }

    /// <summary>
    /// Async version with the same semantics as AgainstDuplicateOperation.
    /// Useful for integration with async pipelines.
    /// </summary>
    public Task AgainstDuplicateOperationAsync(string idempotencyKey, string parameterName = "idempotencyKey")
    {
        AgainstDuplicateOperation(idempotencyKey, parameterName);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Check if a key was already processed (without marking it).
    /// </summary>
    public bool IsProcessed(string idempotencyKey)
    {
        if (_processedKeys.TryGetValue(idempotencyKey, out var timestamp))
            return DateTimeOffset.UtcNow - timestamp < _ttl;
        return false;
    }

    /// <summary>
    /// Manually mark a key as processed.
    /// </summary>
    public void MarkProcessed(string idempotencyKey)
    {
        _processedKeys[idempotencyKey] = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Remove a key (allow reprocessing).
    /// </summary>
    public void Reset(string idempotencyKey)
    {
        _processedKeys.TryRemove(idempotencyKey, out _);
    }

    /// <summary>Clear all tracked keys.</summary>
    public void Clear() => _processedKeys.Clear();

    /// <summary>Number of currently tracked keys.</summary>
    public int Count => _processedKeys.Count;

    private void CleanupIfNeeded()
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastCleanup < TimeSpan.FromMinutes(5) && _processedKeys.Count < _maxKeys)
            return;

        lock (_cleanupLock)
        {
            if (now - _lastCleanup < TimeSpan.FromMinutes(5) && _processedKeys.Count < _maxKeys)
                return;

            var expired = new List<string>();
            foreach (var kvp in _processedKeys)
            {
                if (now - kvp.Value >= _ttl)
                    expired.Add(kvp.Key);
            }
            foreach (var key in expired)
                _processedKeys.TryRemove(key, out _);

            _lastCleanup = now;
        }
    }
}
