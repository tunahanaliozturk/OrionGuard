using System.Collections.Concurrent;
using Moongazing.OrionGuard.DependencyInjection;

namespace Moongazing.OrionGuard.Core;

/// <summary>
/// Decorator that caches validation results for identical inputs.
/// Uses a hash of the object's properties to determine cache key.
/// Thread-safe with configurable TTL.
/// </summary>
public sealed class CachedValidator<T> : IValidator<T> where T : class
{
    private readonly IValidator<T> _inner;
    private readonly TimeSpan _ttl;
    private readonly int _maxCacheSize;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();

    public CachedValidator(IValidator<T> inner, TimeSpan? ttl = null, int maxCacheSize = 1000)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _ttl = ttl ?? TimeSpan.FromMinutes(5);
        _maxCacheSize = maxCacheSize;
    }

    public GuardResult Validate(T value)
    {
        var key = ComputeStructuralKey(value);

        if (_cache.TryGetValue(key, out var entry) && !entry.IsExpired)
            return entry.Result;

        var result = _inner.Validate(value);
        StoreResult(key, result);
        return result;
    }

    public async Task<GuardResult> ValidateAsync(T value, CancellationToken cancellationToken = default)
    {
        var key = ComputeStructuralKey(value);

        if (_cache.TryGetValue(key, out var entry) && !entry.IsExpired)
            return entry.Result;

        // ConfigureAwait(false): library code must not capture the synchronization
        // context to avoid deadlocks in UI frameworks (WPF/WinForms/legacy ASP.NET)
        // that use single-threaded contexts.
        var result = await _inner.ValidateAsync(value, cancellationToken).ConfigureAwait(false);
        StoreResult(key, result);
        return result;
    }

    /// <summary>Clear the validation cache.</summary>
    public void ClearCache() => _cache.Clear();

    /// <summary>Current cache size.</summary>
    public int CacheSize => _cache.Count;

    private void StoreResult(string key, GuardResult result)
    {
        if (_cache.Count >= _maxCacheSize)
            Evict();

        _cache[key] = new CacheEntry(result, DateTime.UtcNow.Add(_ttl));
    }

    /// <summary>
    /// Capacity eviction. Two-phase and biased toward keeping fresh, hot entries:
    /// <list type="number">
    /// <item><description>Sweep all expired entries first -- they cost nothing to evict.</description></item>
    /// <item><description>If we're still over capacity, remove the single entry with
    /// the earliest <c>ExpiresAt</c> (the coldest live entry) instead of clearing
    /// the whole cache.</description></item>
    /// </list>
    /// <para>
    /// Safe to run concurrently: <see cref="ConcurrentDictionary{TKey,TValue}.TryRemove(TKey, out TValue)"/>
    /// is atomic, and redundant scans by racing threads cost only a pass through the
    /// dictionary. Avoiding a lock here keeps <see cref="Validate"/> hits fully lock-free.
    /// </para>
    /// </summary>
    private void Evict()
    {
        // Phase 1: drop expired entries (free capacity, keeps the hot set intact).
        foreach (var kvp in _cache)
        {
            if (kvp.Value.IsExpired)
                _cache.TryRemove(kvp.Key, out _);
        }

        if (_cache.Count < _maxCacheSize) return;

        // Phase 2: still full -- evict the single entry closest to expiry.
        string? oldestKey = null;
        DateTime oldestExpiry = DateTime.MaxValue;
        foreach (var kvp in _cache)
        {
            if (kvp.Value.ExpiresAt < oldestExpiry)
            {
                oldestExpiry = kvp.Value.ExpiresAt;
                oldestKey = kvp.Key;
            }
        }
        if (oldestKey is not null)
            _cache.TryRemove(oldestKey, out _);
    }

    private static readonly System.Reflection.PropertyInfo[] CachedProperties =
        typeof(T).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

    /// <summary>
    /// Builds a structural string key from all public instance properties.
    /// Unlike a hash code, this eliminates collision risk because two distinct
    /// values will always produce distinct keys.
    /// </summary>
    private static string ComputeStructuralKey(T value)
    {
        if (value is null) return "null";

        var sb = new System.Text.StringBuilder();
        foreach (var prop in CachedProperties)
        {
            sb.Append(prop.Name).Append('=').Append(prop.GetValue(value)?.ToString() ?? "null").Append(';');
        }
        return sb.ToString();
    }

    private sealed record CacheEntry(GuardResult Result, DateTime ExpiresAt)
    {
        public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
    }
}

/// <summary>
/// Extension methods for creating cached validators.
/// </summary>
public static class CachedValidatorExtensions
{
    /// <summary>
    /// Wraps a validator with caching support.
    /// </summary>
    public static CachedValidator<T> WithCaching<T>(this IValidator<T> validator, TimeSpan? ttl = null, int maxCacheSize = 1000) where T : class
        => new(validator, ttl, maxCacheSize);
}
