using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Moongazing.OrionGuard.Core;

/// <summary>
/// Thread-safe compiled-regex cache with bounded size and approximate LRU eviction.
/// </summary>
/// <remarks>
/// <para>
/// <b>Design.</b> A <see cref="ConcurrentDictionary{TKey,TValue}"/> gives wait-free
/// hit-path reads -- the common case in validation workloads where a small set of
/// patterns is reused millions of times. Each entry carries an access timestamp
/// updated atomically (64-bit writes are atomic on every .NET 8+ target) without
/// taking any lock. A separate lock is acquired <i>only</i> on the insertion-with-
/// eviction path, which is cold after warmup.
/// </para>
/// <para>
/// <b>LRU accuracy.</b> We implement approximate LRU rather than strict LRU to keep
/// the hit path lock-free. When the cache is full on insertion, we scan once to find
/// the entry with the oldest timestamp and evict it. This is O(N) per eviction but
/// only runs on cache misses after capacity is reached -- negligible in practice.
/// A stale timestamp read (due to a concurrent update we don't observe) can cause
/// a slightly "wrong" victim to be chosen; that is acceptable for a pattern cache.
/// </para>
/// <para>
/// <b>Why this over the previous LRU.</b> The previous implementation used a
/// <see cref="LinkedList{T}"/> + <see cref="Dictionary{TKey,TValue}"/> under a single
/// lock, which serialized every <c>IsMatch</c> call across the process. For hot
/// validation paths (one lookup per HTTP request) this is a real contention point.
/// The current design keeps the hot path scale-free while still preserving hot
/// patterns on overflow.
/// </para>
/// </remarks>
public static class RegexCache
{
    private static readonly TimeSpan DefaultMatchTimeout = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Lock taken only on the insertion-with-eviction path. Hit path is lock-free.
    /// </summary>
    private static readonly object _evictionLock = new();

    private static readonly ConcurrentDictionary<string, CacheEntry> _cache =
        new(StringComparer.Ordinal);

    private sealed class CacheEntry
    {
        public readonly Regex Regex;

        /// <summary>
        /// Last access time in <see cref="Environment.TickCount64"/> ticks.
        /// Non-atomic 64-bit writes are guaranteed atomic by the .NET runtime on
        /// all supported targets (including 32-bit processes), so no Interlocked
        /// is required. A lost update is harmless -- the LRU is approximate.
        /// </summary>
        public long LastAccessTicks;

        public CacheEntry(Regex regex)
        {
            Regex = regex;
            LastAccessTicks = Environment.TickCount64;
        }
    }

    private static int _maxCacheSize = 1000;

    /// <summary>
    /// Maximum number of cached patterns. When exceeded, the entry with the oldest
    /// access time is evicted on the next insertion.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Set to a non-positive value.</exception>
    public static int MaxCacheSize
    {
        get => _maxCacheSize;
        set
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(nameof(value), "MaxCacheSize must be positive.");
            _maxCacheSize = value;
        }
    }

    /// <summary>
    /// Gets or creates a compiled regex for the specified pattern. Thread-safe.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Hot path (cache hit).</b> Lock-free -- a single
    /// <see cref="ConcurrentDictionary{TKey,TValue}.TryGetValue"/> plus one 64-bit
    /// timestamp write. Scales linearly with core count; no thread ever blocks.
    /// </para>
    /// <para>
    /// <b>Cold path (miss).</b> Regex compilation happens outside any lock so
    /// concurrent compilations of <i>different</i> patterns never block each other.
    /// Two threads racing on the <i>same</i> new pattern may each compile once -- the
    /// second's result is discarded by a subsequent <c>TryAdd</c> check, preserving
    /// a single cached instance.
    /// </para>
    /// </remarks>
    public static Regex GetOrCreate(string pattern, RegexOptions options = RegexOptions.Compiled)
    {
        var key = BuildKey(pattern, options);

        // Fast path: lock-free hit
        if (_cache.TryGetValue(key, out var entry))
        {
            entry.LastAccessTicks = Environment.TickCount64;
            return entry.Regex;
        }

        // Miss: compile outside any lock. Expensive but one-time per pattern.
        var compiled = new Regex(pattern, options, DefaultMatchTimeout);
        var newEntry = new CacheEntry(compiled);

        // Insertion under eviction lock -- serialized only among insertions, never among hits.
        lock (_evictionLock)
        {
            // Double-check: another thread may have inserted while we compiled.
            if (_cache.TryGetValue(key, out var existing))
            {
                existing.LastAccessTicks = Environment.TickCount64;
                return existing.Regex;
            }

            if (_cache.Count >= _maxCacheSize)
                EvictOldest();

            _cache[key] = newEntry;
            return compiled;
        }
    }

    /// <summary>
    /// Scans the cache to find and remove the entry with the oldest access timestamp.
    /// O(N) but only called on capacity overflow, which is rare in real workloads.
    /// Must be invoked while holding <see cref="_evictionLock"/>.
    /// </summary>
    private static void EvictOldest()
    {
        string? oldestKey = null;
        long oldestTicks = long.MaxValue;

        foreach (var kvp in _cache)
        {
            var ticks = kvp.Value.LastAccessTicks;
            if (ticks < oldestTicks)
            {
                oldestTicks = ticks;
                oldestKey = kvp.Key;
            }
        }

        if (oldestKey is not null)
            _cache.TryRemove(oldestKey, out _);
    }

    /// <summary>
    /// Checks if a value matches the cached pattern.
    /// </summary>
    public static bool IsMatch(string input, string pattern, RegexOptions options = RegexOptions.Compiled)
    {
        var regex = GetOrCreate(pattern, options);
        return regex.IsMatch(input);
    }

    /// <summary>
    /// Removes all entries from the cache. Takes the eviction lock briefly.
    /// </summary>
    public static void Clear()
    {
        lock (_evictionLock)
        {
            _cache.Clear();
        }
    }

    /// <summary>
    /// Gets the current number of cached patterns.
    /// </summary>
    public static int CacheSize => _cache.Count;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string BuildKey(string pattern, RegexOptions options) =>
        $"{pattern}_{(int)options}";
}

/// <summary>
/// High-performance guard methods optimized for hot paths.
/// Uses span-based operations where possible for zero-allocation validation.
/// </summary>
public static class FastGuard
{
    /// <summary>
    /// Validates that a string is not null or empty using span-based operations.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string NotNullOrEmpty(string? value, string parameterName)
    {
        if (value is null || value.Length == 0)
            ThrowNotNullOrEmpty(parameterName);
        return value!;
    }

    /// <summary>
    /// Validates that a span is not empty.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void NotEmpty<T>(ReadOnlySpan<T> span, string parameterName)
    {
        if (span.IsEmpty)
            ThrowEmpty(parameterName);
    }

    /// <summary>
    /// Validates that an int value is within range using aggressive inlining.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int InRange(int value, int min, int max, string parameterName)
    {
        if ((uint)(value - min) > (uint)(max - min))
            ThrowOutOfRange(parameterName, min, max);
        return value;
    }

    /// <summary>
    /// Validates that a value is positive.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Positive(int value, string parameterName)
    {
        if (value <= 0)
            ThrowNotPositive(parameterName);
        return value;
    }

    /// <summary>
    /// Validates that a value is not null with aggressive inlining.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T NotNull<T>(T? value, string parameterName) where T : class
    {
        if (value is null)
            ThrowNull(parameterName);
        return value!;
    }

    /// <summary>
    /// Validates email format using span-based parsing (no regex allocation).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string Email(string value, string parameterName)
    {
        if (!IsValidEmailSpan(value.AsSpan()))
            ThrowInvalidEmail(parameterName);
        return value;
    }

    /// <summary>
    /// Validates that a string contains only ASCII characters using span.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string Ascii(ReadOnlySpan<char> value, string parameterName)
    {
        foreach (var c in value)
        {
            if (c > 127)
                ThrowArgumentException($"{parameterName} must contain only ASCII characters.", parameterName);
        }
        return value.ToString();
    }

    /// <summary>
    /// Validates that a string contains only alphanumeric characters using span.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void AlphaNumeric(ReadOnlySpan<char> value, string parameterName)
    {
        foreach (var c in value)
        {
            if (!char.IsLetterOrDigit(c))
                ThrowArgumentException($"{parameterName} must contain only alphanumeric characters.", parameterName);
        }
    }

    /// <summary>
    /// Validates that a string contains only digits using span.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void NumericString(ReadOnlySpan<char> value, string parameterName)
    {
        foreach (var c in value)
        {
            if (!char.IsDigit(c))
                ThrowArgumentException($"{parameterName} must contain only digits.", parameterName);
        }
    }

    /// <summary>
    /// Validates maximum string length with span.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void MaxLength(ReadOnlySpan<char> value, int maxLength, string parameterName)
    {
        if (value.Length > maxLength)
            ThrowArgumentException($"{parameterName} must be at most {maxLength} characters.", parameterName);
    }

    /// <summary>
    /// Validates GUID format using span (no allocation).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Guid ValidGuid(ReadOnlySpan<char> value, string parameterName)
    {
        if (!System.Guid.TryParse(value, out var result) || result == System.Guid.Empty)
            ThrowArgumentException($"{parameterName} is not a valid GUID.", parameterName);
        return result;
    }

    /// <summary>
    /// Validates a double is not NaN or Infinity.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Finite(double value, string parameterName)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            ThrowArgumentException($"{parameterName} must be a finite number.", parameterName);
        return value;
    }

    #region Internal span-based helpers

    private static bool IsValidEmailSpan(ReadOnlySpan<char> email)
    {
        if (email.IsEmpty || email.Length > 254)
            return false;

        int atIndex = email.IndexOf('@');
        if (atIndex <= 0 || atIndex >= email.Length - 1)
            return false;

        var local = email[..atIndex];
        var domain = email[(atIndex + 1)..];

        if (local.IsEmpty || domain.IsEmpty)
            return false;

        // Domain must contain at least one dot
        int dotIndex = domain.IndexOf('.');
        if (dotIndex <= 0 || dotIndex >= domain.Length - 1)
            return false;

        // No spaces allowed
        if (email.Contains(' '))
            return false;

        return true;
    }

    #endregion

    #region Throw helpers (separate methods to keep hot path small)

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    [StackTraceHidden]
    private static void ThrowNull(string parameterName) =>
        throw new ArgumentNullException(parameterName);

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    [StackTraceHidden]
    private static void ThrowNotNullOrEmpty(string parameterName) =>
        throw new ArgumentException($"{parameterName} cannot be null or empty.", parameterName);

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    [StackTraceHidden]
    private static void ThrowEmpty(string parameterName) =>
        throw new ArgumentException($"{parameterName} cannot be empty.", parameterName);

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    [StackTraceHidden]
    private static void ThrowOutOfRange(string parameterName, int min, int max) =>
        throw new ArgumentOutOfRangeException(parameterName, $"{parameterName} must be between {min} and {max}.");

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    [StackTraceHidden]
    private static void ThrowNotPositive(string parameterName) =>
        throw new ArgumentOutOfRangeException(parameterName, $"{parameterName} must be positive.");

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    [StackTraceHidden]
    private static void ThrowInvalidEmail(string parameterName) =>
        throw new ArgumentException($"{parameterName} must be a valid email address.", parameterName);

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    [StackTraceHidden]
    private static void ThrowArgumentException(string message, string parameterName) =>
        throw new ArgumentException(message, parameterName);

    #endregion
}
