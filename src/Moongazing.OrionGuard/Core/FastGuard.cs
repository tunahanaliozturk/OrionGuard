using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Moongazing.OrionGuard.Core;

/// <summary>
/// Provides caching for compiled regex patterns to improve performance.
/// Thread-safe with bounded cache size to prevent memory leaks.
/// </summary>
public static class RegexCache
{
    private static readonly ConcurrentDictionary<string, Regex> _cache = new();
    private static readonly TimeSpan DefaultMatchTimeout = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Maximum number of cached patterns. When exceeded, the cache is cleared.
    /// </summary>
    public static int MaxCacheSize { get; set; } = 1000;

    /// <summary>
    /// Gets or creates a compiled regex for the specified pattern.
    /// </summary>
    public static Regex GetOrCreate(string pattern, RegexOptions options = RegexOptions.Compiled)
    {
        var key = $"{pattern}_{(int)options}";
        return _cache.GetOrAdd(key, _ =>
        {
            if (_cache.Count >= MaxCacheSize)
                _cache.Clear();
            return new Regex(pattern, options, DefaultMatchTimeout);
        });
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
