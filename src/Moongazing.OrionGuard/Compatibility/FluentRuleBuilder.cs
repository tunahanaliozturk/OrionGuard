using System.Collections;
using System.Linq.Expressions;
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
        // Validators are usually registered as transient, so this constructor runs for every RuleFor on
        // every resolution. The shared cache compiles a member selector once per process instead.
        _accessor = AccessorCache<T, TProperty?>.Get(expression);
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
    /// Validates that the property value is not empty, as FluentValidation defines it: not null, not an empty or
    /// whitespace-only string, not an empty collection or sequence, and not the default value of its type
    /// (<c>0</c>, <see cref="Guid.Empty"/>, <see cref="DateTime.MinValue"/>, <c>false</c>).
    /// </summary>
    public FluentRuleBuilder<T, TProperty> NotEmpty()
    {
        _rules.Add(instance =>
            IsEmpty(_accessor(instance))
                ? new ValidationError(_propertyName, $"'{_propertyName}' must not be empty.", "NOT_EMPTY")
                : null);
        return this;
    }

    /// <summary>
    /// Validates that a string property does not exceed the specified maximum length.
    /// </summary>
    /// <param name="max">The maximum allowed length (inclusive).</param>
    public FluentRuleBuilder<T, TProperty> MaximumLength(int max)
    {
        _rules.Add(instance => MaximumLengthError(_accessor(instance), max));
        return this;
    }

    /// <summary>
    /// Validates that a string property meets the specified minimum length.
    /// </summary>
    /// <param name="min">The minimum required length (inclusive).</param>
    public FluentRuleBuilder<T, TProperty> MinimumLength(int min)
    {
        _rules.Add(instance => MinimumLengthError(_accessor(instance), min));
        return this;
    }

    /// <summary>
    /// Validates that a string property length falls within the specified range.
    /// </summary>
    /// <param name="min">The minimum required length (inclusive).</param>
    /// <param name="max">The maximum allowed length (inclusive).</param>
    public FluentRuleBuilder<T, TProperty> Length(int min, int max)
    {
        // One rule for both bounds: WithMessage and WithErrorCode wrap only the last rule, so two rules left a
        // too-short value with the default minimum-length message.
        _rules.Add(instance =>
        {
            var value = _accessor(instance);
            return MinimumLengthError(value, min) ?? MaximumLengthError(value, max);
        });
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
            if (value is string s && !string.IsNullOrWhiteSpace(s) && !FormatRules.IsEmail(s))
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
            if (value is string s && !string.IsNullOrWhiteSpace(s) && !FormatRules.IsMatch(RegexCache.GetOrCreate(pattern), s))
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
            Passes(_accessor(instance), threshold, static comparison => comparison > 0)
                ? null
                : new ValidationError(_propertyName, $"'{_propertyName}' must be greater than '{threshold}'.", "GREATER_THAN"));
        return this;
    }

    /// <summary>
    /// Validates that the property value is less than the specified threshold.
    /// </summary>
    /// <param name="threshold">The exclusive upper bound.</param>
    public FluentRuleBuilder<T, TProperty> LessThan(IComparable threshold)
    {
        _rules.Add(instance =>
            Passes(_accessor(instance), threshold, static comparison => comparison < 0)
                ? null
                : new ValidationError(_propertyName, $"'{_propertyName}' must be less than '{threshold}'.", "LESS_THAN"));
        return this;
    }

    /// <summary>
    /// Validates that the property value is greater than or equal to the specified threshold.
    /// </summary>
    /// <param name="threshold">The inclusive lower bound.</param>
    public FluentRuleBuilder<T, TProperty> GreaterThanOrEqualTo(IComparable threshold)
    {
        _rules.Add(instance =>
            Passes(_accessor(instance), threshold, static comparison => comparison >= 0)
                ? null
                : new ValidationError(_propertyName, $"'{_propertyName}' must be greater than or equal to '{threshold}'.", "GREATER_THAN_OR_EQUAL"));
        return this;
    }

    /// <summary>
    /// Validates that the property value is less than or equal to the specified threshold.
    /// </summary>
    /// <param name="threshold">The inclusive upper bound.</param>
    public FluentRuleBuilder<T, TProperty> LessThanOrEqualTo(IComparable threshold)
    {
        _rules.Add(instance =>
            Passes(_accessor(instance), threshold, static comparison => comparison <= 0)
                ? null
                : new ValidationError(_propertyName, $"'{_propertyName}' must be less than or equal to '{threshold}'.", "LESS_THAN_OR_EQUAL"));
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
            return Passes(value, from, static comparison => comparison >= 0) && Passes(value, to, static comparison => comparison <= 0)
                ? null
                : new ValidationError(_propertyName, $"'{_propertyName}' must be between {from} and {to} (inclusive).", "INCLUSIVE_BETWEEN");
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
            return Passes(value, from, static comparison => comparison > 0) && Passes(value, to, static comparison => comparison < 0)
                ? null
                : new ValidationError(_propertyName, $"'{_propertyName}' must be between {from} and {to} (exclusive).", "EXCLUSIVE_BETWEEN");
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

    /// <summary>
    /// FluentValidation's NotEmpty check, case for case, so migrated validators keep their verdicts.
    /// </summary>
    private static bool IsEmpty(TProperty? value)
    {
        switch (value)
        {
            case null:
                return true;
            case string text:
                return string.IsNullOrWhiteSpace(text);
            case ICollection collection:
                return collection.Count == 0;
            case IEnumerable sequence:
                var enumerator = sequence.GetEnumerator();
                using (enumerator as IDisposable)
                {
                    return !enumerator.MoveNext();
                }
            default:
                // The value-type defaults: 0, Guid.Empty, DateTime.MinValue, false. For Nullable<T> the default is
                // null, so a nullable property holding 0 is not empty, exactly as in FluentValidation.
                return EqualityComparer<TProperty?>.Default.Equals(value, default);
        }
    }

    /// <summary>
    /// True when a comparison rule accepts <paramref name="value"/>. A null value passes, as in FluentValidation
    /// (pair the rule with NotNull to require a value). Numbers compare by value across numeric types, so the
    /// boxed <c>int</c> of <c>GreaterThan(0)</c> works on a <c>decimal</c> property; <c>int.CompareTo</c> threw
    /// "Object must be of type Decimal" there. A pair that cannot be ordered (NaN, a null threshold, unrelated
    /// types) fails the rule instead of throwing.
    /// </summary>
    private static bool Passes(TProperty? value, IComparable? threshold, Func<int, bool> accepts) =>
        value is null || (NumericComparer.TryCompare(value, threshold, out var comparison) && accepts(comparison));

    private ValidationError? MinimumLengthError(TProperty? value, int min) =>
        value is string text && text.Length < min
            ? new ValidationError(_propertyName, $"'{_propertyName}' must be at least {min} characters.", "MIN_LENGTH")
            : null;

    private ValidationError? MaximumLengthError(TProperty? value, int max) =>
        value is string text && text.Length > max
            ? new ValidationError(_propertyName, $"'{_propertyName}' must be {max} characters or fewer.", "MAX_LENGTH")
            : null;

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
