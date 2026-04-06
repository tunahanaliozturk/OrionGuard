using System.Linq.Expressions;

namespace Moongazing.OrionGuard.Core;

/// <summary>
/// Fluent DSL for cross-property validation rules.
/// Validates relationships between multiple properties on the same object.
/// </summary>
public sealed class CrossPropertyValidator<T> where T : class
{
    private readonly T _instance;
    private readonly List<ValidationError> _errors = new();

    public CrossPropertyValidator(T instance)
    {
        _instance = instance ?? throw new ArgumentNullException(nameof(instance));
    }

    /// <summary>
    /// Asserts that two properties are equal.
    /// Usage: .AreEqual(u => u.Password, u => u.ConfirmPassword)
    /// </summary>
    public CrossPropertyValidator<T> AreEqual<TProp>(
        Expression<Func<T, TProp>> left,
        Expression<Func<T, TProp>> right,
        string? message = null)
    {
        var leftName = GetName(left);
        var rightName = GetName(right);
        var leftVal = left.Compile()(_instance);
        var rightVal = right.Compile()(_instance);

        if (!Equals(leftVal, rightVal))
            _errors.Add(new ValidationError($"{leftName},{rightName}",
                message ?? $"{leftName} must be equal to {rightName}.", "CROSS_EQUAL"));
        return this;
    }

    /// <summary>
    /// Asserts that two properties are NOT equal.
    /// Usage: .AreNotEqual(u => u.Email, u => u.Username)
    /// </summary>
    public CrossPropertyValidator<T> AreNotEqual<TProp>(
        Expression<Func<T, TProp>> left,
        Expression<Func<T, TProp>> right,
        string? message = null)
    {
        var leftName = GetName(left);
        var rightName = GetName(right);
        var leftVal = left.Compile()(_instance);
        var rightVal = right.Compile()(_instance);

        if (Equals(leftVal, rightVal))
            _errors.Add(new ValidationError($"{leftName},{rightName}",
                message ?? $"{leftName} must not be equal to {rightName}.", "CROSS_NOT_EQUAL"));
        return this;
    }

    /// <summary>
    /// Asserts that left property is greater than right property.
    /// Usage: .IsGreaterThan(o => o.EndDate, o => o.StartDate)
    /// </summary>
    public CrossPropertyValidator<T> IsGreaterThan<TProp>(
        Expression<Func<T, TProp>> left,
        Expression<Func<T, TProp>> right,
        string? message = null) where TProp : IComparable<TProp>
    {
        var leftName = GetName(left);
        var rightName = GetName(right);
        var leftVal = left.Compile()(_instance);
        var rightVal = right.Compile()(_instance);

        if (leftVal is not null && rightVal is not null && leftVal.CompareTo(rightVal) <= 0)
            _errors.Add(new ValidationError($"{leftName},{rightName}",
                message ?? $"{leftName} must be greater than {rightName}.", "CROSS_GREATER_THAN"));
        return this;
    }

    /// <summary>
    /// Asserts that left is less than right.
    /// </summary>
    public CrossPropertyValidator<T> IsLessThan<TProp>(
        Expression<Func<T, TProp>> left,
        Expression<Func<T, TProp>> right,
        string? message = null) where TProp : IComparable<TProp>
    {
        var leftName = GetName(left);
        var rightName = GetName(right);
        var leftVal = left.Compile()(_instance);
        var rightVal = right.Compile()(_instance);

        if (leftVal is not null && rightVal is not null && leftVal.CompareTo(rightVal) >= 0)
            _errors.Add(new ValidationError($"{leftName},{rightName}",
                message ?? $"{leftName} must be less than {rightName}.", "CROSS_LESS_THAN"));
        return this;
    }

    /// <summary>
    /// At least one of the properties must be non-null/non-empty.
    /// Usage: .AtLeastOneRequired(u => u.Phone, u => u.Email)
    /// </summary>
    public CrossPropertyValidator<T> AtLeastOneRequired<TProp1, TProp2>(
        Expression<Func<T, TProp1>> first,
        Expression<Func<T, TProp2>> second,
        string? message = null)
    {
        var firstName = GetName(first);
        var secondName = GetName(second);
        var firstVal = first.Compile()(_instance);
        var secondVal = second.Compile()(_instance);

        var firstEmpty = firstVal is null || (firstVal is string s1 && string.IsNullOrWhiteSpace(s1));
        var secondEmpty = secondVal is null || (secondVal is string s2 && string.IsNullOrWhiteSpace(s2));

        if (firstEmpty && secondEmpty)
            _errors.Add(new ValidationError($"{firstName},{secondName}",
                message ?? $"At least one of {firstName} or {secondName} is required.", "AT_LEAST_ONE"));
        return this;
    }

    /// <summary>
    /// Custom cross-property predicate.
    /// </summary>
    public CrossPropertyValidator<T> Must(Func<T, bool> predicate, string propertyNames, string message, string? errorCode = null)
    {
        if (!predicate(_instance))
            _errors.Add(new ValidationError(propertyNames, message, errorCode));
        return this;
    }

    /// <summary>
    /// Conditional cross-property validation.
    /// </summary>
    public CrossPropertyValidator<T> When(Func<T, bool> condition, Action<CrossPropertyValidator<T>> configure)
    {
        if (condition(_instance))
            configure(this);
        return this;
    }

    /// <summary>
    /// Returns the validation result.
    /// </summary>
    public GuardResult ToResult() => _errors.Count == 0 ? GuardResult.Success() : GuardResult.Failure(_errors);

    /// <summary>
    /// Throws if any cross-property validation errors were found.
    /// </summary>
    public void ThrowIfInvalid() => ToResult().ThrowIfInvalid();

    private static string GetName<TProp>(Expression<Func<T, TProp>> expr)
    {
        if (expr.Body is MemberExpression m) return m.Member.Name;
        if (expr.Body is UnaryExpression { Operand: MemberExpression um }) return um.Member.Name;
        return expr.ToString();
    }
}
