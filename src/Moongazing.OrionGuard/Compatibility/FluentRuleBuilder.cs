using System.Linq.Expressions;
using System.Text.RegularExpressions;
using Moongazing.OrionGuard.Core;
using Moongazing.OrionGuard.Utilities;

namespace Moongazing.OrionGuard.Compatibility;

/// <summary>
/// FluentValidation-compatible rule builder for easy migration.
/// Provides familiar method names: NotEmpty(), MaximumLength(), EmailAddress(), etc.
/// <para>
/// Usage mirrors FluentValidation:
/// <code>
/// RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
/// RuleFor(x => x.Email).NotEmpty().EmailAddress();
/// RuleFor(x => x.Age).InclusiveBetween(18, 120);
/// </code>
/// </para>
/// </summary>
/// <typeparam name="T">The type being validated.</typeparam>
/// <typeparam name="TProperty">The type of the property being validated.</typeparam>
public sealed class FluentRuleBuilder<T, TProperty>
{
    private readonly Func<T, TProperty?> _accessor;
    private readonly string _propertyName;
    private readonly List<Func<T, ValidationError?>> _rules = new();

    internal FluentRuleBuilder(Expression<Func<T, TProperty?>> expression)
    {
        _accessor = expression.Compile();
        _propertyName = GetPropertyName(expression);
    }

    #region String Validators

    /// <summary>
    /// Validates that the property value is not null.
    /// </summary>
    public FluentRuleBuilder<T, TProperty> NotNull()
    {
        _rules.Add(instance =>
        {
            var value = _accessor(instance);
            return value is null
                ? new ValidationError(_propertyName, $"'{_propertyName}' must not be empty.", "NOT_NULL")
                : null;
        });
        return this;
    }

    /// <summary>
    /// Validates that the property value is not null, empty, or whitespace (for strings).
    /// </summary>
    public FluentRuleBuilder<T, TProperty> NotEmpty()
    {
        _rules.Add(instance =>
        {
            var value = _accessor(instance);
            var isEmpty = value switch
            {
                null => true,
                string s => string.IsNullOrWhiteSpace(s),
                _ => false
            };
            return isEmpty
                ? new ValidationError(_propertyName, $"'{_propertyName}' must not be empty.", "NOT_EMPTY")
                : null;
        });
        return this;
    }

    /// <summary>
    /// Validates that a string property does not exceed the specified maximum length.
    /// </summary>
    /// <param name="max">The maximum allowed length (inclusive).</param>
    public FluentRuleBuilder<T, TProperty> MaximumLength(int max)
    {
        _rules.Add(instance =>
        {
            var value = _accessor(instance);
            if (value is string s && s.Length > max)
            {
                return new ValidationError(_propertyName, $"'{_propertyName}' must be {max} characters or fewer.", "MAX_LENGTH");
            }

            return null;
        });
        return this;
    }

    /// <summary>
    /// Validates that a string property meets the specified minimum length.
    /// </summary>
    /// <param name="min">The minimum required length (inclusive).</param>
    public FluentRuleBuilder<T, TProperty> MinimumLength(int min)
    {
        _rules.Add(instance =>
        {
            var value = _accessor(instance);
            if (value is string s && s.Length < min)
            {
                return new ValidationError(_propertyName, $"'{_propertyName}' must be at least {min} characters.", "MIN_LENGTH");
            }

            return null;
        });
        return this;
    }

    /// <summary>
    /// Validates that a string property length falls within the specified range.
    /// </summary>
    /// <param name="min">The minimum required length (inclusive).</param>
    /// <param name="max">The maximum allowed length (inclusive).</param>
    public FluentRuleBuilder<T, TProperty> Length(int min, int max)
    {
        MinimumLength(min);
        MaximumLength(max);
        return this;
    }

    /// <summary>
    /// Validates that a string property contains a valid email address.
    /// Uses the same source-generated regex pattern as OrionGuard core.
    /// </summary>
    public FluentRuleBuilder<T, TProperty> EmailAddress()
    {
        _rules.Add(instance =>
        {
            var value = _accessor(instance);
            if (value is string s && !string.IsNullOrWhiteSpace(s) && !GeneratedRegexPatterns.Email().IsMatch(s))
            {
                return new ValidationError(_propertyName, $"'{_propertyName}' is not a valid email address.", "INVALID_EMAIL");
            }

            return null;
        });
        return this;
    }

    /// <summary>
    /// Validates that a string property matches the specified regular expression pattern.
    /// </summary>
    /// <param name="pattern">The regex pattern to match against.</param>
    public FluentRuleBuilder<T, TProperty> Matches(string pattern)
    {
        _rules.Add(instance =>
        {
            var value = _accessor(instance);
            if (value is string s && !string.IsNullOrWhiteSpace(s) && !Regex.IsMatch(s, pattern))
            {
                return new ValidationError(_propertyName, $"'{_propertyName}' is not in the correct format.", "PATTERN");
            }

            return null;
        });
        return this;
    }

    #endregion

    #region Comparison Validators

    /// <summary>
    /// Validates that the property value is greater than the specified threshold.
    /// </summary>
    /// <param name="threshold">The exclusive lower bound.</param>
    public FluentRuleBuilder<T, TProperty> GreaterThan(IComparable threshold)
    {
        _rules.Add(instance =>
        {
            var value = _accessor(instance);
            if (value is IComparable comparable && comparable.CompareTo(threshold) <= 0)
            {
                return new ValidationError(_propertyName, $"'{_propertyName}' must be greater than '{threshold}'.", "GREATER_THAN");
            }

            return null;
        });
        return this;
    }

    /// <summary>
    /// Validates that the property value is less than the specified threshold.
    /// </summary>
    /// <param name="threshold">The exclusive upper bound.</param>
    public FluentRuleBuilder<T, TProperty> LessThan(IComparable threshold)
    {
        _rules.Add(instance =>
        {
            var value = _accessor(instance);
            if (value is IComparable comparable && comparable.CompareTo(threshold) >= 0)
            {
                return new ValidationError(_propertyName, $"'{_propertyName}' must be less than '{threshold}'.", "LESS_THAN");
            }

            return null;
        });
        return this;
    }

    /// <summary>
    /// Validates that the property value is greater than or equal to the specified threshold.
    /// </summary>
    /// <param name="threshold">The inclusive lower bound.</param>
    public FluentRuleBuilder<T, TProperty> GreaterThanOrEqualTo(IComparable threshold)
    {
        _rules.Add(instance =>
        {
            var value = _accessor(instance);
            if (value is IComparable comparable && comparable.CompareTo(threshold) < 0)
            {
                return new ValidationError(_propertyName, $"'{_propertyName}' must be greater than or equal to '{threshold}'.", "GREATER_THAN_OR_EQUAL");
            }

            return null;
        });
        return this;
    }

    /// <summary>
    /// Validates that the property value is less than or equal to the specified threshold.
    /// </summary>
    /// <param name="threshold">The inclusive upper bound.</param>
    public FluentRuleBuilder<T, TProperty> LessThanOrEqualTo(IComparable threshold)
    {
        _rules.Add(instance =>
        {
            var value = _accessor(instance);
            if (value is IComparable comparable && comparable.CompareTo(threshold) > 0)
            {
                return new ValidationError(_propertyName, $"'{_propertyName}' must be less than or equal to '{threshold}'.", "LESS_THAN_OR_EQUAL");
            }

            return null;
        });
        return this;
    }

    /// <summary>
    /// Validates that the property value falls within the specified inclusive range.
    /// </summary>
    /// <param name="from">The inclusive lower bound.</param>
    /// <param name="to">The inclusive upper bound.</param>
    public FluentRuleBuilder<T, TProperty> InclusiveBetween(IComparable from, IComparable to)
    {
        _rules.Add(instance =>
        {
            var value = _accessor(instance);
            if (value is IComparable comparable && (comparable.CompareTo(from) < 0 || comparable.CompareTo(to) > 0))
            {
                return new ValidationError(_propertyName, $"'{_propertyName}' must be between {from} and {to} (inclusive).", "INCLUSIVE_BETWEEN");
            }

            return null;
        });
        return this;
    }

    /// <summary>
    /// Validates that the property value falls within the specified exclusive range.
    /// </summary>
    /// <param name="from">The exclusive lower bound.</param>
    /// <param name="to">The exclusive upper bound.</param>
    public FluentRuleBuilder<T, TProperty> ExclusiveBetween(IComparable from, IComparable to)
    {
        _rules.Add(instance =>
        {
            var value = _accessor(instance);
            if (value is IComparable comparable && (comparable.CompareTo(from) <= 0 || comparable.CompareTo(to) >= 0))
            {
                return new ValidationError(_propertyName, $"'{_propertyName}' must be between {from} and {to} (exclusive).", "EXCLUSIVE_BETWEEN");
            }

            return null;
        });
        return this;
    }

    /// <summary>
    /// Validates that the property value is equal to the specified value.
    /// </summary>
    /// <param name="comparisonValue">The value to compare against.</param>
    public FluentRuleBuilder<T, TProperty> Equal(TProperty comparisonValue)
    {
        _rules.Add(instance =>
        {
            var value = _accessor(instance);
            if (!EqualityComparer<TProperty>.Default.Equals(value!, comparisonValue))
            {
                return new ValidationError(_propertyName, $"'{_propertyName}' must be equal to '{comparisonValue}'.", "EQUAL");
            }

            return null;
        });
        return this;
    }

    /// <summary>
    /// Validates that the property value is not equal to the specified value.
    /// </summary>
    /// <param name="comparisonValue">The value to compare against.</param>
    public FluentRuleBuilder<T, TProperty> NotEqual(TProperty comparisonValue)
    {
        _rules.Add(instance =>
        {
            var value = _accessor(instance);
            if (EqualityComparer<TProperty>.Default.Equals(value!, comparisonValue))
            {
                return new ValidationError(_propertyName, $"'{_propertyName}' must not be equal to '{comparisonValue}'.", "NOT_EQUAL");
            }

            return null;
        });
        return this;
    }

    #endregion

    #region Custom Validators

    /// <summary>
    /// Validates the property value using a custom predicate.
    /// </summary>
    /// <param name="predicate">A function that returns true if the value is valid.</param>
    public FluentRuleBuilder<T, TProperty> Must(Func<TProperty?, bool> predicate)
    {
        _rules.Add(instance =>
        {
            var value = _accessor(instance);
            return !predicate(value)
                ? new ValidationError(_propertyName, $"'{_propertyName}' does not satisfy the specified condition.", "PREDICATE")
                : null;
        });
        return this;
    }

    #endregion

    #region Message & Code Overrides

    /// <summary>
    /// Overrides the error message of the last added rule.
    /// </summary>
    /// <param name="message">The custom error message.</param>
    public FluentRuleBuilder<T, TProperty> WithMessage(string message)
    {
        if (_rules.Count == 0)
        {
            return this;
        }

        var lastRule = _rules[^1];
        _rules[^1] = instance =>
        {
            var error = lastRule(instance);
            return error is not null
                ? error with { Message = message }
                : null;
        };
        return this;
    }

    /// <summary>
    /// Overrides the error code of the last added rule.
    /// </summary>
    /// <param name="code">The custom error code.</param>
    public FluentRuleBuilder<T, TProperty> WithErrorCode(string code)
    {
        if (_rules.Count == 0)
        {
            return this;
        }

        var lastRule = _rules[^1];
        _rules[^1] = instance =>
        {
            var error = lastRule(instance);
            return error is not null
                ? error with { ErrorCode = code }
                : null;
        };
        return this;
    }

    #endregion

    #region Conditional Validators

    /// <summary>
    /// Makes all previously defined rules on this builder conditional.
    /// Rules only execute when the condition returns true.
    /// </summary>
    /// <param name="condition">A function evaluated against the validated instance.</param>
    public FluentRuleBuilder<T, TProperty> When(Func<T, bool> condition)
    {
        for (var i = 0; i < _rules.Count; i++)
        {
            var rule = _rules[i];
            _rules[i] = instance => condition(instance) ? rule(instance) : null;
        }

        return this;
    }

    /// <summary>
    /// Makes all previously defined rules on this builder conditional (inverse).
    /// Rules only execute when the condition returns false.
    /// </summary>
    /// <param name="condition">A function evaluated against the validated instance.</param>
    public FluentRuleBuilder<T, TProperty> Unless(Func<T, bool> condition)
    {
        return When(instance => !condition(instance));
    }

    #endregion

    #region Internal

    /// <summary>
    /// Builds and returns the list of validation rule delegates.
    /// </summary>
    internal List<Func<T, ValidationError?>> Build() => _rules;

    private static string GetPropertyName(Expression<Func<T, TProperty?>> expression)
    {
        if (expression.Body is MemberExpression memberExpression)
        {
            return memberExpression.Member.Name;
        }

        if (expression.Body is UnaryExpression { Operand: MemberExpression unaryMember })
        {
            return unaryMember.Member.Name;
        }

        return expression.ToString();
    }

    #endregion
}
