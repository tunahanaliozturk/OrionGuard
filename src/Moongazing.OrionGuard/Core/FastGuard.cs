using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace Moongazing.OrionGuard.Core;

/// <summary>
/// Provides caching for compiled regex patterns to improve performance.
/// Thread-safe and designed for high-throughput scenarios.
/// </summary>
public static class RegexCache
{
    private static readonly ConcurrentDictionary<string, Regex> _cache = new();
    private static readonly TimeSpan DefaultMatchTimeout = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets or creates a compiled regex for the specified pattern.
    /// </summary>
    public static Regex GetOrCreate(string pattern, RegexOptions options = RegexOptions.Compiled)
    {
        var key = $"{pattern}_{(int)options}";
        return _cache.GetOrAdd(key, _ => new Regex(pattern, options, DefaultMatchTimeout));
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
    /// Clears the regex cache.
    /// </summary>
    public static void Clear() => _cache.Clear();

    /// <summary>
    /// Gets the current cache size.
    /// </summary>
    public static int CacheSize => _cache.Count;
}

/// <summary>
/// High-performance guard methods optimized for hot paths.
/// Uses span-based operations where possible for zero-allocation validation.
/// </summary>
public static class FastGuard
{
    /// <summary>
    /// Validates that a string is not null or empty using span-based operations.
    /// More efficient than string.IsNullOrEmpty for hot paths.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static void NotNullOrEmpty(string? value, string parameterName)
    {
        if (value is null || value.Length == 0)
        {
            ThrowNullOrEmpty(parameterName);
        }
    }

    /// <summary>
    /// Validates that a span is not empty.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static void NotEmpty<T>(ReadOnlySpan<T> span, string parameterName)
    {
        if (span.IsEmpty)
        {
            ThrowEmpty(parameterName);
        }
    }

    /// <summary>
    /// Validates that a value is within range using aggressive inlining.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static void InRange(int value, int min, int max, string parameterName)
    {
        if (value < min || value > max)
        {
            ThrowOutOfRange(parameterName, min, max);
        }
    }

    /// <summary>
    /// Validates that a value is positive.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static void Positive(int value, string parameterName)
    {
        if (value <= 0)
        {
            ThrowNotPositive(parameterName);
        }
    }

    /// <summary>
    /// Validates that a value is not null with aggressive inlining.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static T NotNull<T>(T? value, string parameterName) where T : class
    {
        if (value is null)
        {
            ThrowNull(parameterName);
        }
        return value!;
    }

    /// <summary>
    /// Validates email format using cached regex.
    /// </summary>
    public static void Email(string value, string parameterName)
    {
        if (!RegexCache.IsMatch(value, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
        {
            ThrowInvalidEmail(parameterName);
        }
    }

    // Separate throw methods to keep the fast path small for inlining
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void ThrowNull(string parameterName) =>
        throw new ArgumentNullException(parameterName);

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void ThrowNullOrEmpty(string parameterName) =>
        throw new ArgumentException($"{parameterName} cannot be null or empty.", parameterName);

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void ThrowEmpty(string parameterName) =>
        throw new ArgumentException($"{parameterName} cannot be empty.", parameterName);

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void ThrowOutOfRange(string parameterName, int min, int max) =>
        throw new ArgumentOutOfRangeException(parameterName, $"{parameterName} must be between {min} and {max}.");

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void ThrowNotPositive(string parameterName) =>
        throw new ArgumentOutOfRangeException(parameterName, $"{parameterName} must be positive.");

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void ThrowInvalidEmail(string parameterName) =>
        throw new ArgumentException($"{parameterName} must be a valid email address.", parameterName);
}
