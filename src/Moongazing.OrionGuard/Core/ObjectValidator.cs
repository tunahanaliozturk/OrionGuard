using System.Linq.Expressions;
using System.Reflection;

namespace Moongazing.OrionGuard.Core;

/// <summary>
/// Object validator that validates all properties of an object at once.
/// </summary>
public sealed class ObjectValidator<T> where T : class
{
    private readonly T _instance;
    private readonly List<ValidationError> _errors = new();
    private readonly bool _throwOnFirstError;

    internal ObjectValidator(T instance, bool throwOnFirstError)
    {
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
        var value = selector.Compile()(_instance);

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
    public static ObjectValidator<T> Object<T>(T instance) where T : class
    {
        return new ObjectValidator<T>(instance, throwOnFirstError: false);
    }

    /// <summary>
    /// Creates an object validator that throws on first error.
    /// </summary>
    public static ObjectValidator<T> ObjectStrict<T>(T instance) where T : class
    {
        return new ObjectValidator<T>(instance, throwOnFirstError: true);
    }

    /// <summary>
    /// Validates multiple objects and combines their results.
    /// </summary>
    public static GuardResult All(params GuardResult[] results)
    {
        return GuardResult.Combine(results);
    }
}
