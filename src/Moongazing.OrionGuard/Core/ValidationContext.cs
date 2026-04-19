using System.Collections.Immutable;

namespace Moongazing.OrionGuard.Core;

/// <summary>
/// Immutable bag of contextual data that validation rules can read to make decisions
/// that depend on information outside the object being validated (tenant id, current
/// user role, feature flags, trace id, etc.).
/// </summary>
/// <remarks>
/// <para>
/// <b>Design.</b> The context is a typed key-value store built on
/// <see cref="ImmutableDictionary{TKey, TValue}"/> so rule closures can capture it by
/// value without worrying about concurrent mutation. Each <c>With*</c> call returns a
/// new instance -- the original is never mutated.
/// </para>
/// <para>
/// <b>Typed access.</b> Use <see cref="Get{T}(string)"/> and <see cref="TryGet{T}(string, out T)"/>
/// to retrieve values with compile-time type safety. The internal store holds <c>object</c>
/// values; type mismatches throw <see cref="InvalidCastException"/>.
/// </para>
/// <para>
/// <b>Strongly-typed keys.</b> For repeatedly-used keys, prefer
/// <see cref="ValidationContextKey{T}"/> which bundles the key name and expected type.
/// </para>
/// </remarks>
public sealed class ValidationContext
{
    /// <summary>
    /// An empty context with no values. Use this as a starting point or when no context is available.
    /// </summary>
    public static readonly ValidationContext Empty = new(ImmutableDictionary<string, object?>.Empty);

    private readonly ImmutableDictionary<string, object?> _values;

    private ValidationContext(ImmutableDictionary<string, object?> values) => _values = values;

    /// <summary>
    /// Returns a new <see cref="ValidationContext"/> with the specified key/value set.
    /// Existing keys are replaced. Never mutates the current instance.
    /// </summary>
    public ValidationContext With(string key, object? value)
    {
        ArgumentNullException.ThrowIfNull(key);
        return new ValidationContext(_values.SetItem(key, value));
    }

    /// <summary>
    /// Returns a new <see cref="ValidationContext"/> with the specified strongly-typed
    /// key/value set.
    /// </summary>
    public ValidationContext With<T>(ValidationContextKey<T> key, T value)
    {
        ArgumentNullException.ThrowIfNull(key);
        return new ValidationContext(_values.SetItem(key.Name, value));
    }

    /// <summary>
    /// Gets the value associated with <paramref name="key"/>.
    /// </summary>
    /// <exception cref="KeyNotFoundException">The key was not present.</exception>
    /// <exception cref="InvalidCastException">The stored value is not assignable to <typeparamref name="T"/>.</exception>
    public T Get<T>(string key)
    {
        if (!_values.TryGetValue(key, out var raw))
            throw new KeyNotFoundException($"ValidationContext does not contain key '{key}'.");
        return (T)raw!;
    }

    /// <summary>
    /// Gets the value associated with the strongly-typed key.
    /// </summary>
    public T Get<T>(ValidationContextKey<T> key) => Get<T>(key.Name);

    /// <summary>
    /// Attempts to retrieve a value. Returns <c>false</c> if the key is missing or the
    /// stored value is not assignable to <typeparamref name="T"/>.
    /// </summary>
    public bool TryGet<T>(string key, out T value)
    {
        if (_values.TryGetValue(key, out var raw) && raw is T typed)
        {
            value = typed;
            return true;
        }
        value = default!;
        return false;
    }

    /// <summary>
    /// Attempts to retrieve a value using a strongly-typed key.
    /// </summary>
    public bool TryGet<T>(ValidationContextKey<T> key, out T value) => TryGet(key.Name, out value);

    /// <summary>
    /// Returns <c>true</c> if the specified key is present in the context.
    /// </summary>
    public bool Contains(string key) => _values.ContainsKey(key);

    /// <summary>
    /// Gets the number of key/value pairs in the context.
    /// </summary>
    public int Count => _values.Count;
}

/// <summary>
/// Strongly-typed key for <see cref="ValidationContext"/>. Bundles the string key with
/// its expected value type so callers do not have to repeat generic arguments.
/// </summary>
/// <typeparam name="T">The expected value type.</typeparam>
public sealed class ValidationContextKey<T>
{
    /// <summary>The underlying string key.</summary>
    public string Name { get; }

    /// <summary>
    /// Creates a typed key with the specified name.
    /// </summary>
    public ValidationContextKey(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        Name = name;
    }

    public static implicit operator string(ValidationContextKey<T> key) => key.Name;
}
