using System.Collections.Concurrent;
using System.Linq.Expressions;

namespace Moongazing.OrionGuard.Core;

/// <summary>
/// Represents a snapshot pair (<see cref="Original"/>, <see cref="Updated"/>) used for
/// PATCH-style partial validation. Only properties that actually changed between the two
/// instances are validated, letting callers skip rules for untouched fields.
/// </summary>
/// <typeparam name="T">The type of the object being patched.</typeparam>
/// <remarks>
/// <para>
/// <b>Use case.</b> REST <c>PATCH</c>, GraphQL partial mutations, optimistic-concurrency
/// updates -- any scenario where the caller wants to validate only the fields they
/// actually modified, without forcing the request model to carry every property.
/// </para>
/// <para>
/// <b>Change detection.</b> Per-property equality uses
/// <see cref="EqualityComparer{T}.Default"/>. This correctly handles value types,
/// reference types (via <see cref="object.Equals(object?)"/>), and records (structural
/// equality). Deep object graph comparison is intentionally out of scope -- use
/// <see cref="NestedValidator{T}"/> for that.
/// </para>
/// </remarks>
public sealed class Delta<T> where T : class
{
    /// <summary>
    /// The original (pre-change) snapshot. May be <c>null</c> when the entity is being created.
    /// </summary>
    public T? Original { get; }

    /// <summary>
    /// The updated (post-change) snapshot.
    /// </summary>
    public T Updated { get; }

    /// <summary>
    /// Creates a delta from an original/updated snapshot pair.
    /// </summary>
    /// <param name="original">The pre-change state, or <c>null</c> for new entities.</param>
    /// <param name="updated">The post-change state. Must not be null.</param>
    public Delta(T? original, T updated)
    {
        ArgumentNullException.ThrowIfNull(updated);
        Original = original;
        Updated = updated;
    }

    /// <summary>
    /// Returns <c>true</c> if the value at <paramref name="selector"/> differs between
    /// <see cref="Original"/> and <see cref="Updated"/>. When <see cref="Original"/> is
    /// <c>null</c> (creation scenario) every property is treated as changed.
    /// </summary>
    public bool HasChanged<TProperty>(Expression<Func<T, TProperty>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        if (Original is null) return true;

        var accessor = AccessorCache<TProperty>.Get(selector);
        var original = accessor(Original);
        var updated = accessor(Updated);
        return !EqualityComparer<TProperty>.Default.Equals(original, updated);
    }

    /// <summary>
    /// Enumerates the names of all properties that changed between the two snapshots.
    /// Useful for logging or audit trails. Enumerates via reflection (cached per type).
    /// </summary>
    public IEnumerable<string> GetChangedPropertyNames()
    {
        if (Original is null)
        {
            foreach (var prop in PropertyCache.Get(typeof(T)))
                yield return prop.Name;
            yield break;
        }

        foreach (var prop in PropertyCache.Get(typeof(T)))
        {
            var oldVal = prop.GetValue(Original);
            var newVal = prop.GetValue(Updated);
            if (!Equals(oldVal, newVal))
                yield return prop.Name;
        }
    }

    /// <summary>
    /// Per-(T, TProperty) compiled accessor cache keyed by <see cref="Expression.ToString()"/>.
    /// The generic static class is partitioned per closed generic type, so lookups never
    /// collide across property types.
    /// </summary>
    internal static class AccessorCache<TProperty>
    {
        private static readonly ConcurrentDictionary<string, Func<T, TProperty>> Compiled = new();

        public static Func<T, TProperty> Get(Expression<Func<T, TProperty>> selector)
        {
            var key = selector.ToString();
            if (Compiled.TryGetValue(key, out var cached)) return cached;
            return Compiled.GetOrAdd(key, static (_, sel) => sel.Compile(), selector);
        }
    }

    private static class PropertyCache
    {
        private static readonly ConcurrentDictionary<Type, System.Reflection.PropertyInfo[]> Cache = new();

        public static System.Reflection.PropertyInfo[] Get(Type type) =>
            Cache.GetOrAdd(type, static t =>
                t.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance));
    }
}

/// <summary>
/// Fluent delta validator. Rules attached via <c>ForChanged</c> run only when the target
/// property actually changed; <c>WhenChanged</c> runs a block of rules conditionally.
/// </summary>
public sealed class DeltaValidator<T> where T : class
{
    private readonly Delta<T> _delta;
    private readonly List<ValidationError> _errors = new();

    internal DeltaValidator(Delta<T> delta)
    {
        ArgumentNullException.ThrowIfNull(delta);
        _delta = delta;
    }

    /// <summary>
    /// Runs the rules configured by <paramref name="configure"/> against the <i>updated</i>
    /// snapshot only when <paramref name="selector"/>'s property has changed relative to the
    /// original. Unchanged properties are skipped entirely.
    /// </summary>
    public DeltaValidator<T> ForChanged<TProperty>(
        Expression<Func<T, TProperty>> selector,
        Action<FluentGuard<TProperty>> configure)
    {
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(configure);

        if (!_delta.HasChanged(selector)) return this;

        var accessor = Delta<T>.AccessorCache<TProperty>.Get(selector);
        var value = accessor(_delta.Updated);
        var propertyName = GetPropertyName(selector);

        var guard = new FluentGuard<TProperty>(value, propertyName, throwOnFirstError: false);
        configure(guard);

        var result = guard.ToResult();
        if (result.IsInvalid)
            _errors.AddRange(result.Errors);

        return this;
    }

    /// <summary>
    /// Runs <paramref name="configure"/> against the updated snapshot only if the target
    /// property changed. Unlike <see cref="ForChanged{TProperty}"/>, the callback has full
    /// access to the current <see cref="DeltaValidator{T}"/> so multiple related rules can
    /// be grouped under a single "changed" guard.
    /// </summary>
    public DeltaValidator<T> WhenChanged<TProperty>(
        Expression<Func<T, TProperty>> selector,
        Action<DeltaValidator<T>> configure)
    {
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(configure);

        if (_delta.HasChanged(selector))
            configure(this);

        return this;
    }

    /// <summary>
    /// Adds an ad-hoc rule that runs unconditionally (independent of delta state).
    /// </summary>
    public DeltaValidator<T> Must(Func<T, bool> predicate, string propertyName, string message)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        if (!predicate(_delta.Updated))
            _errors.Add(new ValidationError(propertyName, message));

        return this;
    }

    /// <summary>
    /// Returns the accumulated <see cref="GuardResult"/>.
    /// </summary>
    public GuardResult ToResult() =>
        _errors.Count == 0 ? GuardResult.Success() : GuardResult.Failure(_errors);

    /// <summary>
    /// Throws an <see cref="AggregateValidationException"/> if any rule failed; otherwise
    /// returns the updated snapshot for fluent continuation.
    /// </summary>
    public T ThrowIfInvalid()
    {
        if (_errors.Count > 0)
            throw new AggregateValidationException(_errors);
        return _delta.Updated;
    }

    private static string GetPropertyName<TProperty>(Expression<Func<T, TProperty>> expression)
    {
        if (expression.Body is MemberExpression memberExpression)
            return memberExpression.Member.Name;

        if (expression.Body is UnaryExpression { Operand: MemberExpression operandMember })
            return operandMember.Member.Name;

        return "Unknown";
    }
}
