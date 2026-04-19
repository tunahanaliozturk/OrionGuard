using System.Linq.Expressions;
using Moongazing.OrionGuard.Utilities;

namespace Moongazing.OrionGuard.Core;

/// <summary>
/// Provides fluent nested/hierarchical object validation with unlimited depth.
/// Supports validating complex object graphs with parent-child relationships.
/// </summary>
public sealed class NestedValidator<T> where T : class
{
    private readonly T _instance;
    private readonly string _pathPrefix;
    private readonly List<ValidationError> _errors = new();

    public NestedValidator(T instance, string pathPrefix = "")
    {
        _instance = instance ?? throw new ArgumentNullException(nameof(instance));
        _pathPrefix = pathPrefix;
    }

    /// <summary>
    /// Validates a simple property using a fluent property validation builder.
    /// </summary>
    /// <typeparam name="TProperty">The type of the property to validate.</typeparam>
    /// <param name="selector">Expression selecting the property to validate.</param>
    /// <param name="configure">Action to configure validation rules on the property.</param>
    /// <returns>This validator instance for method chaining.</returns>
    public NestedValidator<T> Property<TProperty>(
        Expression<Func<T, TProperty>> selector,
        Action<PropertyValidationBuilder<TProperty>> configure)
    {
        var memberName = GetMemberName(selector);
        var fullPath = string.IsNullOrEmpty(_pathPrefix) ? memberName : $"{_pathPrefix}.{memberName}";
        var compiled = selector.Compile();
        var value = compiled(_instance);

        var builder = new PropertyValidationBuilder<TProperty>(value, fullPath);
        configure(builder);
        _errors.AddRange(builder.GetErrors());

        return this;
    }

    /// <summary>
    /// Navigates into a nested object for deep validation.
    /// If the nested object is null, a NOT_NULL validation error is added automatically.
    /// </summary>
    /// <typeparam name="TChild">The type of the nested child object.</typeparam>
    /// <param name="selector">Expression selecting the nested object.</param>
    /// <param name="configure">Action to configure validation rules on the nested object.</param>
    /// <returns>This validator instance for method chaining.</returns>
    /// <example>
    /// <code>
    /// Validate.Nested(order)
    ///     .Nested(o => o.Address, address => address
    ///         .Property(a => a.City, p => p.NotEmpty())
    ///         .Property(a => a.ZipCode, p => p.Length(5, 10)))
    ///     .ToResult();
    /// </code>
    /// </example>
    public NestedValidator<T> Nested<TChild>(
        Expression<Func<T, TChild?>> selector,
        Action<NestedValidator<TChild>> configure) where TChild : class
    {
        var memberName = GetMemberName(selector);
        var fullPath = string.IsNullOrEmpty(_pathPrefix) ? memberName : $"{_pathPrefix}.{memberName}";
        var compiled = selector.Compile();
        var child = compiled(_instance);

        if (child is null)
        {
            _errors.Add(new ValidationError(fullPath, $"{fullPath} cannot be null.", "NOT_NULL"));
            return this;
        }

        var nestedValidator = new NestedValidator<TChild>(child, fullPath);
        configure(nestedValidator);
        _errors.AddRange(nestedValidator._errors);

        return this;
    }

    /// <summary>
    /// Validates each item in a collection with nested validation.
    /// If the collection is null, a NOT_NULL validation error is added automatically.
    /// Each item's path includes its zero-based index (e.g., "Items[0].Name").
    /// </summary>
    /// <typeparam name="TItem">The type of each item in the collection.</typeparam>
    /// <param name="selector">Expression selecting the collection property.</param>
    /// <param name="configure">Action to configure validation rules for each item, receiving the item validator and zero-based index.</param>
    /// <returns>This validator instance for method chaining.</returns>
    /// <example>
    /// <code>
    /// Validate.Nested(order)
    ///     .Collection(o => o.Items, (item, index) => item
    ///         .Property(i => i.Name, p => p.NotEmpty())
    ///         .Property(i => i.Quantity, p => p.GreaterThan(0)))
    ///     .ToResult();
    /// </code>
    /// </example>
    public NestedValidator<T> Collection<TItem>(
        Expression<Func<T, IEnumerable<TItem>?>> selector,
        Action<NestedValidator<TItem>, int> configure) where TItem : class
    {
        var memberName = GetMemberName(selector);
        var fullPath = string.IsNullOrEmpty(_pathPrefix) ? memberName : $"{_pathPrefix}.{memberName}";
        var compiled = selector.Compile();
        var collection = compiled(_instance);

        if (collection is null)
        {
            _errors.Add(new ValidationError(fullPath, $"{fullPath} cannot be null.", "NOT_NULL"));
            return this;
        }

        var index = 0;
        foreach (var item in collection)
        {
            var itemPath = $"{fullPath}[{index}]";
            if (item is null)
            {
                _errors.Add(new ValidationError(itemPath, $"{itemPath} cannot be null.", "NOT_NULL"));
            }
            else
            {
                var itemValidator = new NestedValidator<TItem>(item, itemPath);
                configure(itemValidator, index);
                _errors.AddRange(itemValidator._errors);
            }
            index++;
        }

        return this;
    }

    /// <summary>
    /// Adds a custom validation predicate at the current nesting level.
    /// </summary>
    /// <param name="predicate">A function that returns true if the object is valid.</param>
    /// <param name="message">The error message if validation fails.</param>
    /// <param name="errorCode">Optional error code for programmatic error handling.</param>
    /// <returns>This validator instance for method chaining.</returns>
    public NestedValidator<T> Must(Func<T, bool> predicate, string message, string? errorCode = null)
    {
        if (!predicate(_instance))
        {
            var path = string.IsNullOrEmpty(_pathPrefix) ? typeof(T).Name : _pathPrefix;
            _errors.Add(new ValidationError(path, message, errorCode));
        }
        return this;
    }

    /// <summary>
    /// Conditionally applies validation rules based on a predicate evaluated against the current object.
    /// </summary>
    /// <param name="condition">A function that determines whether the nested rules should be applied.</param>
    /// <param name="configure">Action to configure conditional validation rules.</param>
    /// <returns>This validator instance for method chaining.</returns>
    public NestedValidator<T> When(Func<T, bool> condition, Action<NestedValidator<T>> configure)
    {
        if (condition(_instance))
        {
            configure(this);
        }
        return this;
    }

    /// <summary>
    /// Returns the accumulated validation result.
    /// </summary>
    /// <returns>A <see cref="GuardResult"/> representing the validation outcome.</returns>
    public GuardResult ToResult()
    {
        return _errors.Count == 0 ? GuardResult.Success() : GuardResult.Failure(_errors);
    }

    /// <summary>
    /// Throws an <see cref="AggregateValidationException"/> if any validation errors were accumulated.
    /// </summary>
    /// <returns>The validated instance if all validations passed.</returns>
    public T ThrowIfInvalid()
    {
        var result = ToResult();
        result.ThrowIfInvalid();
        return _instance;
    }

    private static string GetMemberName<TMember>(Expression<Func<T, TMember>> expression)
    {
        if (expression.Body is MemberExpression member)
            return member.Member.Name;
        if (expression.Body is UnaryExpression { Operand: MemberExpression unaryMember })
            return unaryMember.Member.Name;
        throw new ArgumentException("Expression must be a member access expression.");
    }
}

/// <summary>
/// Fluent builder for property-level validation rules within <see cref="NestedValidator{T}"/>.
/// Provides common validation methods (null checks, length, range, email, custom predicates).
/// </summary>
/// <typeparam name="TProperty">The type of the property being validated.</typeparam>
public sealed class PropertyValidationBuilder<TProperty>
{
    private readonly TProperty? _value;
    private readonly string _path;
    private readonly List<ValidationError> _errors = new();

    internal PropertyValidationBuilder(TProperty? value, string path)
    {
        _value = value;
        _path = path;
    }

    /// <summary>
    /// Validates that the property value is not null.
    /// </summary>
    public PropertyValidationBuilder<TProperty> NotNull()
    {
        if (_value is null)
            _errors.Add(new ValidationError(_path, $"{_path} cannot be null.", "NOT_NULL"));
        return this;
    }

    /// <summary>
    /// Validates that the property value is not null or whitespace (for strings) or not null (for other types).
    /// </summary>
    public PropertyValidationBuilder<TProperty> NotEmpty()
    {
        if (_value is null || (_value is string s && string.IsNullOrWhiteSpace(s)))
            _errors.Add(new ValidationError(_path, $"{_path} cannot be empty.", "NOT_EMPTY"));
        return this;
    }

    /// <summary>
    /// Validates that a string property length falls within the specified range.
    /// </summary>
    /// <param name="min">Minimum allowed length (inclusive).</param>
    /// <param name="max">Maximum allowed length (inclusive).</param>
    public PropertyValidationBuilder<TProperty> Length(int min, int max)
    {
        if (_value is string s && (s.Length < min || s.Length > max))
            _errors.Add(new ValidationError(_path, $"{_path} must be between {min} and {max} characters.", "LENGTH"));
        return this;
    }

    /// <summary>
    /// Validates that a string property has at least the specified minimum length.
    /// </summary>
    /// <param name="min">Minimum allowed length (inclusive).</param>
    public PropertyValidationBuilder<TProperty> MinLength(int min)
    {
        if (_value is string s && s.Length < min)
            _errors.Add(new ValidationError(_path, $"{_path} must be at least {min} characters.", "MIN_LENGTH"));
        return this;
    }

    /// <summary>
    /// Validates that the property value is strictly greater than the specified threshold.
    /// Generic overload avoids boxing for value types.
    /// </summary>
    /// <typeparam name="TValue">The comparable type of the threshold.</typeparam>
    /// <param name="threshold">The exclusive lower bound.</param>
    public PropertyValidationBuilder<TProperty> GreaterThan<TValue>(TValue threshold) where TValue : IComparable<TValue>
    {
        if (_value is TValue comparable && comparable.CompareTo(threshold) <= 0)
            _errors.Add(new ValidationError(_path, $"{_path} must be greater than {threshold}.", "GREATER_THAN"));
        return this;
    }

    /// <summary>
    /// Validates that the property value is strictly greater than the specified threshold.
    /// </summary>
    /// <param name="threshold">The exclusive lower bound.</param>
    public PropertyValidationBuilder<TProperty> GreaterThan(IComparable threshold)
    {
        if (_value is IComparable comparable && comparable.CompareTo(threshold) <= 0)
            _errors.Add(new ValidationError(_path, $"{_path} must be greater than {threshold}.", "GREATER_THAN"));
        return this;
    }

    /// <summary>
    /// Validates that the property value is strictly less than the specified threshold.
    /// Generic overload avoids boxing for value types.
    /// </summary>
    /// <typeparam name="TValue">The comparable type of the threshold.</typeparam>
    /// <param name="threshold">The exclusive upper bound.</param>
    public PropertyValidationBuilder<TProperty> LessThan<TValue>(TValue threshold) where TValue : IComparable<TValue>
    {
        if (_value is TValue comparable && comparable.CompareTo(threshold) >= 0)
            _errors.Add(new ValidationError(_path, $"{_path} must be less than {threshold}.", "LESS_THAN"));
        return this;
    }

    /// <summary>
    /// Validates that the property value is strictly less than the specified threshold.
    /// </summary>
    /// <param name="threshold">The exclusive upper bound.</param>
    public PropertyValidationBuilder<TProperty> LessThan(IComparable threshold)
    {
        if (_value is IComparable comparable && comparable.CompareTo(threshold) >= 0)
            _errors.Add(new ValidationError(_path, $"{_path} must be less than {threshold}.", "LESS_THAN"));
        return this;
    }

    /// <summary>
    /// Validates that the property value falls within the specified inclusive range.
    /// Generic overload avoids boxing for value types.
    /// </summary>
    /// <typeparam name="TValue">The comparable type of the bounds.</typeparam>
    /// <param name="min">The inclusive lower bound.</param>
    /// <param name="max">The inclusive upper bound.</param>
    public PropertyValidationBuilder<TProperty> InRange<TValue>(TValue min, TValue max) where TValue : IComparable<TValue>
    {
        if (_value is TValue comparable && (comparable.CompareTo(min) < 0 || comparable.CompareTo(max) > 0))
            _errors.Add(new ValidationError(_path, $"{_path} must be between {min} and {max}.", "IN_RANGE"));
        return this;
    }

    /// <summary>
    /// Validates that the property value falls within the specified inclusive range.
    /// </summary>
    /// <param name="min">The inclusive lower bound.</param>
    /// <param name="max">The inclusive upper bound.</param>
    public PropertyValidationBuilder<TProperty> InRange(IComparable min, IComparable max)
    {
        if (_value is IComparable comparable && (comparable.CompareTo(min) < 0 || comparable.CompareTo(max) > 0))
            _errors.Add(new ValidationError(_path, $"{_path} must be between {min} and {max}.", "IN_RANGE"));
        return this;
    }

    /// <summary>
    /// Validates that a string property contains a valid email address format.
    /// Uses the library's <see cref="GeneratedRegexPatterns.Email"/> compiled regex.
    /// </summary>
    public PropertyValidationBuilder<TProperty> Email()
    {
        if (_value is string s && !string.IsNullOrWhiteSpace(s) && !GeneratedRegexPatterns.Email().IsMatch(s))
            _errors.Add(new ValidationError(_path, $"{_path} must be a valid email.", "INVALID_EMAIL"));
        return this;
    }

    /// <summary>
    /// Validates the property value against a custom predicate.
    /// </summary>
    /// <param name="predicate">A function that returns true if the value is valid.</param>
    /// <param name="message">The error message if validation fails.</param>
    /// <param name="errorCode">Optional error code for programmatic error handling.</param>
    public PropertyValidationBuilder<TProperty> Must(Func<TProperty?, bool> predicate, string message, string? errorCode = null)
    {
        if (!predicate(_value))
            _errors.Add(new ValidationError(_path, message, errorCode));
        return this;
    }

    internal List<ValidationError> GetErrors() => _errors;
}
