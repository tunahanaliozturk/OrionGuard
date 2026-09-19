using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Moongazing.OrionGuard.DependencyInjection;

namespace Moongazing.OrionGuard.Core;

/// <summary>
/// Decorator that caches validation results for equal inputs.
/// Thread-safe with configurable TTL.
/// </summary>
/// <remarks>
/// <para>
/// <b>Cache key.</b> A cached result is reused only when the cache can prove the input is the same:
/// </para>
/// <list type="bullet">
/// <item><description>With a key selector (<see cref="CachedValidatorExtensions.WithCaching{T, TKey}"/>),
/// the key is the selector's result. It must cover every value the inner validator reads.</description></item>
/// <item><description>Without one, a record whose equality is compiler-synthesized is its own key: that
/// equality compares every member. Members are compared shallowly, so a record holding a mutable collection
/// that changes between calls still needs a key selector.</description></item>
/// <item><description>Any other <typeparamref name="T"/> bypasses the cache and always runs the inner
/// validator. Reference equality cannot tell whether a mutable object changed since it was cached, and a
/// hand-written <see cref="IEquatable{T}"/> (for example an entity compared by Id) can call two inputs equal
/// while the validated state differs.</description></item>
/// </list>
/// <para>
/// A call with a non-empty <see cref="ValidationContext"/> also bypasses the cache, because rules may read
/// the context (tenant, role, feature flags) and a result computed for one context must not be served to
/// another.
/// </para>
/// </remarks>
public sealed class CachedValidator<T> : IValidator<T> where T : class
{
    private static readonly bool IsSelfKeyed = HasSynthesizedValueEquality(typeof(T));

    private readonly IValidator<T> _inner;
    private readonly Func<T, object?>? _keySelector;
    private readonly TimeSpan _ttl;
    private readonly int _maxCacheSize;
    private readonly ConcurrentDictionary<object, CacheEntry> _cache = new(ValueKeyComparer.Instance);

    // Approximate entry count so a cache miss does not pay for ConcurrentDictionary.Count, which takes
    // every internal lock. Races can make it drift; Evict() resynchronises it from the real count.
    private int _count;

    public CachedValidator(IValidator<T> inner, TimeSpan? ttl = null, int maxCacheSize = 1000)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _ttl = ttl ?? TimeSpan.FromMinutes(5);
        _maxCacheSize = maxCacheSize;
    }

    internal CachedValidator(IValidator<T> inner, Func<T, object?> keySelector, TimeSpan? ttl, int maxCacheSize)
        : this(inner, ttl, maxCacheSize)
    {
        _keySelector = keySelector ?? throw new ArgumentNullException(nameof(keySelector));
    }

    public GuardResult Validate(T value) => Validate(value, ValidationContext.Empty);

    public GuardResult Validate(T value, ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var key = GetKey(value, context);
        if (key is null)
            return _inner.Validate(value, context);

        if (_cache.TryGetValue(key, out var entry) && !entry.IsExpired)
            return entry.Result;

        var result = _inner.Validate(value, context);
        StoreResult(key, result);
        return result;
    }

    public Task<GuardResult> ValidateAsync(T value, CancellationToken cancellationToken = default) =>
        ValidateAsync(value, ValidationContext.Empty, cancellationToken);

    public async Task<GuardResult> ValidateAsync(T value, ValidationContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        // ConfigureAwait(false): library code must not capture the synchronization
        // context to avoid deadlocks in UI frameworks (WPF/WinForms/legacy ASP.NET)
        // that use single-threaded contexts.
        var key = GetKey(value, context);
        if (key is null)
            return await _inner.ValidateAsync(value, context, cancellationToken).ConfigureAwait(false);

        if (_cache.TryGetValue(key, out var entry) && !entry.IsExpired)
            return entry.Result;

        var result = await _inner.ValidateAsync(value, context, cancellationToken).ConfigureAwait(false);
        StoreResult(key, result);
        return result;
    }

    /// <summary>Clear the validation cache.</summary>
    public void ClearCache()
    {
        _cache.Clear();
        Interlocked.Exchange(ref _count, 0);
    }

    /// <summary>Current cache size.</summary>
    public int CacheSize => _cache.Count;

    // Records get a compiler-synthesized Equals(T) marked [CompilerGenerated]; a hand-written one is not.
    private static bool HasSynthesizedValueEquality(Type type) =>
        type.GetMethod(nameof(Equals), BindingFlags.Public | BindingFlags.Instance, [type])
            ?.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false) == true;

    /// <summary>
    /// Returns the cache key for <paramref name="value"/>, or <c>null</c> when this call must not use the
    /// cache (no provable identity, or a context the rules might read).
    /// </summary>
    private object? GetKey(T value, ValidationContext context)
    {
        if (value is null || context.Count > 0)
            return null;

        if (_keySelector is not null)
            return _keySelector(value);

        return IsSelfKeyed ? value : null;
    }

    private void StoreResult(object key, GuardResult result)
    {
        if (Volatile.Read(ref _count) >= _maxCacheSize)
            Evict();

        var entry = new CacheEntry(result, DateTime.UtcNow.Add(_ttl));
        if (_cache.TryAdd(key, entry))
        {
            Interlocked.Increment(ref _count);
        }
        else
        {
            // The key is already present (an expired entry, or a concurrent miss): refresh it in place.
            _cache[key] = entry;
        }
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
    /// dictionary. Avoiding a lock here keeps <see cref="Validate(T)"/> hits fully lock-free.
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

        // Eviction already scans the whole cache, so resynchronising the approximate count here is free.
        var count = _cache.Count;
        Volatile.Write(ref _count, count);
        if (count < _maxCacheSize) return;

        // Phase 2: still full -- evict the single entry closest to expiry.
        object? oldestKey = null;
        DateTime oldestExpiry = DateTime.MaxValue;
        foreach (var kvp in _cache)
        {
            if (kvp.Value.ExpiresAt < oldestExpiry)
            {
                oldestExpiry = kvp.Value.ExpiresAt;
                oldestKey = kvp.Key;
            }
        }
        if (oldestKey is not null && _cache.TryRemove(oldestKey, out _))
            Interlocked.Decrement(ref _count);
    }

    private sealed record CacheEntry(GuardResult Result, DateTime ExpiresAt)
    {
        public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
    }

    /// <summary>
    /// Compares <typeparamref name="T"/> keys with <see cref="EqualityComparer{T}.Default"/> (so an
    /// <see cref="IEquatable{T}"/> implementation is honoured exactly) and selector keys with their own
    /// <see cref="object.Equals(object?)"/>.
    /// </summary>
    private sealed class ValueKeyComparer : IEqualityComparer<object>
    {
        public static readonly ValueKeyComparer Instance = new();

        public new bool Equals(object? x, object? y) =>
            x is T left && y is T right
                ? EqualityComparer<T>.Default.Equals(left, right)
                : object.Equals(x, y);

        public int GetHashCode(object obj) =>
            obj is T value ? EqualityComparer<T>.Default.GetHashCode(value) : obj.GetHashCode();
    }
}

/// <summary>
/// Extension methods for creating cached validators.
/// </summary>
public static class CachedValidatorExtensions
{
    /// <summary>
    /// Wraps a validator with caching support. Results are cached only when <typeparamref name="T"/> is a
    /// record with compiler-synthesized equality; other types, including types with a hand-written
    /// <see cref="IEquatable{T}"/>, are validated on every call. Use <see cref="WithCaching{T, TKey}"/> to
    /// cache any type by an explicit key that covers the validated state.
    /// </summary>
    public static CachedValidator<T> WithCaching<T>(this IValidator<T> validator, TimeSpan? ttl = null, int maxCacheSize = 1000) where T : class
        => new(validator, ttl, maxCacheSize);

    /// <summary>
    /// Wraps a validator with caching support, caching results by the key that
    /// <paramref name="keySelector"/> returns for each input.
    /// </summary>
    /// <param name="validator">The validator to wrap.</param>
    /// <param name="keySelector">
    /// Returns the cache key for an input. Two inputs with equal keys share one cached result, so the key
    /// must cover every value the inner validator reads, e.g. <c>order =&gt; (order.Id, order.Version)</c>.
    /// A <c>null</c> key bypasses the cache for that call.
    /// </param>
    /// <param name="ttl">How long a result stays cached. Defaults to five minutes.</param>
    /// <param name="maxCacheSize">The soft upper bound on cached entries.</param>
    public static CachedValidator<T> WithCaching<T, TKey>(
        this IValidator<T> validator,
        Func<T, TKey> keySelector,
        TimeSpan? ttl = null,
        int maxCacheSize = 1000)
        where T : class
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(keySelector);
        return new(validator, value => keySelector(value), ttl, maxCacheSize);
    }
}
