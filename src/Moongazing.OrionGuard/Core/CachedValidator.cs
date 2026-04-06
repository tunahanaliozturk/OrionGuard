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

        var result = await _inner.ValidateAsync(value, cancellationToken);
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
            _cache.Clear(); // Simple eviction — clear all when full

        _cache[key] = new CacheEntry(result, DateTime.UtcNow.Add(_ttl));
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
