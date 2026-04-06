using System.Collections.Concurrent;
using System.Linq.Expressions;

namespace Moongazing.OrionGuard.Core;

/// <summary>
/// Object validator that validates all properties of an object at once.
/// Uses compiled expression caching to avoid repeated compilation overhead.
/// </summary>
public sealed class ObjectValidator<T> where T : class
{
    private static readonly ConcurrentDictionary<string, Delegate> _compiledAccessors = new();

    private readonly T _instance;
    private readonly List<ValidationError> _errors = new();
    private readonly bool _throwOnFirstError;

    internal ObjectValidator(T instance, bool throwOnFirstError)
    {
        ArgumentNullException.ThrowIfNull(instance);
        _instance = instance;
        _throwOnFirstError = throwOnFirstError;
    }

    /// <summary>
    /// Validates a specific property of the object.
    /// </summary>
    public ObjectValidator<T> Property<TProperty>(
        Expression<Func<T, TProperty>> selector,
        Action<FluentGuard<TProperty>> configure)
    {
        var propertyName = GetPropertyName(selector);
        var accessor = GetOrCompileAccessor(selector, propertyName);
        var value = accessor(_instance);

        var guard = new FluentGuard<TProperty>(value, propertyName, throwOnFirstError: false);
        configure(guard);

        var result = guard.ToResult();
        if (result.IsInvalid)
        {
            _errors.AddRange(result.Errors);

            if (_throwOnFirstError)
            {
                throw new AggregateValidationException(_errors);
            }
        }

        return this;
    }

    /// <summary>
    /// Validates a property against another property (cross-property validation).
    /// </summary>
    public ObjectValidator<T> CrossProperty<TProp1, TProp2>(
        Expression<Func<T, TProp1>> selector1,
        Expression<Func<T, TProp2>> selector2,
        Func<TProp1, TProp2, bool> predicate,
        string message,
        string? errorCode = null)
    {
        var name1 = GetPropertyName(selector1);
        var name2 = GetPropertyName(selector2);
        var value1 = GetOrCompileAccessor(selector1, name1)(_instance);
        var value2 = GetOrCompileAccessor(selector2, name2)(_instance);

        if (!predicate(value1, value2))
        {
            _errors.Add(new ValidationError($"{name1},{name2}", message, errorCode ?? "CROSS_PROPERTY"));

            if (_throwOnFirstError)
            {
                throw new AggregateValidationException(_errors);
            }
        }

        return this;
    }

    /// <summary>
    /// Validates that a property is not null.
    /// </summary>
    public ObjectValidator<T> NotNull<TProperty>(
        Expression<Func<T, TProperty>> selector,
        string? message = null) where TProperty : class
    {
        return Property(selector, g => g.NotNull(message));
    }

    /// <summary>
    /// Validates that a string property is not empty.
    /// </summary>
    public ObjectValidator<T> NotEmpty(
        Expression<Func<T, string?>> selector,
        string? message = null)
    {
        return Property(selector, g => g.NotNull().NotEmpty(message));
    }

    /// <summary>
    /// Validates that a property satisfies a condition.
    /// </summary>
    public ObjectValidator<T> Must<TProperty>(
        Expression<Func<T, TProperty>> selector,
        Func<TProperty, bool> predicate,
        string message)
    {
        return Property(selector, g => g.Must(predicate, message));
    }

    /// <summary>
    /// Conditionally applies validation rules.
    /// </summary>
    public ObjectValidator<T> When(bool condition, Action<ObjectValidator<T>> configure)
    {
        if (condition)
        {
            configure(this);
        }
        return this;
    }

    /// <summary>
    /// Returns the validation result.
    /// </summary>
    public GuardResult ToResult()
    {
        return _errors.Count == 0 ? GuardResult.Success() : GuardResult.Failure(_errors);
    }

    /// <summary>
    /// Throws if validation failed.
    /// </summary>
    public T ThrowIfInvalid()
    {
        if (_errors.Count > 0)
        {
            throw new AggregateValidationException(_errors);
        }
        return _instance;
    }

    /// <summary>
    /// Returns the validated object if valid.
    /// </summary>
    public T Build() => ThrowIfInvalid();

    private static Func<T, TProperty> GetOrCompileAccessor<TProperty>(
        Expression<Func<T, TProperty>> selector, string cacheKey)
    {
        var key = $"{typeof(T).FullName}.{cacheKey}.{typeof(TProperty).FullName}";
        var cached = _compiledAccessors.GetOrAdd(key, _ => selector.Compile());
        return (Func<T, TProperty>)cached;
    }

    private static string GetPropertyName<TProperty>(Expression<Func<T, TProperty>> expression)
    {
        if (expression.Body is MemberExpression memberExpression)
        {
            return memberExpression.Member.Name;
        }

        if (expression.Body is UnaryExpression unaryExpression &&
            unaryExpression.Operand is MemberExpression operandMember)
        {
            return operandMember.Member.Name;
        }

        return "Unknown";
    }
}

/// <summary>
/// Entry point for object validation.
/// </summary>
public static class Validate
{
    /// <summary>
    /// Creates an object validator for the specified instance.
    /// </summary>
    public static ObjectValidator<T> For<T>(T instance) where T : class
    {
        return new ObjectValidator<T>(instance, throwOnFirstError: false);
    }

    /// <summary>
    /// Creates an object validator that throws on first error.
    /// </summary>
    public static ObjectValidator<T> ForStrict<T>(T instance) where T : class
    {
        return new ObjectValidator<T>(instance, throwOnFirstError: true);
    }

    /// <summary>
    /// Creates a nested validator for deep hierarchical object graph validation.
    /// Supports unlimited-depth validation with full property path tracking.
    /// </summary>
    /// <typeparam name="T">The type of the root object to validate.</typeparam>
    /// <param name="instance">The object instance to validate.</param>
    /// <returns>A <see cref="NestedValidator{T}"/> for fluent configuration.</returns>
    /// <example>
    /// <code>
    /// var result = Validate.Nested(order)
    ///     .Property(o => o.OrderNumber, p => p.NotEmpty())
    ///     .Nested(o => o.Customer, customer => customer
    ///         .Property(c => c.Email, p => p.Email())
    ///         .Nested(c => c.Address, address => address
    ///             .Property(a => a.City, p => p.NotEmpty())))
    ///     .Collection(o => o.Items, (item, index) => item
    ///         .Property(i => i.ProductName, p => p.NotEmpty())
    ///         .Property(i => i.Quantity, p => p.GreaterThan(0)))
    ///     .ToResult();
    /// </code>
    /// </example>
    public static NestedValidator<T> Nested<T>(T instance) where T : class
    {
        return new NestedValidator<T>(instance);
    }

    /// <summary>
    /// Creates a fluent cross-property validator for the specified instance.
    /// Usage: Validate.CrossProperties(order).IsGreaterThan(o => o.EndDate, o => o.StartDate).ThrowIfInvalid();
    /// </summary>
    public static CrossPropertyValidator<T> CrossProperties<T>(T instance) where T : class => new(instance);

    /// <summary>
    /// Creates a polymorphic validator for the specified base type.
    /// Usage: Validate.Polymorphic&lt;PaymentBase&gt;().When&lt;CreditCard&gt;(cc => ...).Validate(payment);
    /// </summary>
    public static PolymorphicValidator<T> Polymorphic<T>() where T : class => new();

    /// <summary>
    /// Validates multiple objects and combines their results.
    /// </summary>
    public static GuardResult All(params GuardResult[] results)
    {
        return GuardResult.Combine(results);
    }
}
