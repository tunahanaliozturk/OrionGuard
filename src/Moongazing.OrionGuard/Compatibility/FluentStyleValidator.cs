using System.Linq.Expressions;
using Moongazing.OrionGuard.Core;
using Moongazing.OrionGuard.DependencyInjection;

namespace Moongazing.OrionGuard.Compatibility;

/// <summary>
/// Base class for FluentValidation-style validators.
/// Drop-in replacement that uses the same <c>RuleFor().NotEmpty().EmailAddress()</c> syntax.
/// <para>
/// <b>Migration from FluentValidation:</b> Change <c>using FluentValidation</c> to
/// <c>using Moongazing.OrionGuard.Compatibility</c> and inherit from
/// <see cref="FluentStyleValidator{T}"/> instead of FluentValidation's AbstractValidator.
/// Your existing validator constructors will work unchanged.
/// </para>
/// <example>
/// <code>
/// public class UserValidator : FluentStyleValidator&lt;User&gt;
/// {
///     public UserValidator()
///     {
///         RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
///         RuleFor(x => x.Email).NotEmpty().EmailAddress();
///         RuleFor(x => x.Age).InclusiveBetween(18, 120);
///     }
/// }
/// </code>
/// </example>
/// </summary>
/// <typeparam name="T">The type of object being validated.</typeparam>
public abstract class FluentStyleValidator<T> : IValidator<T> where T : class
{
    private readonly List<Func<List<Func<T, ValidationError?>>>> _builders = new();
    private List<Func<T, ValidationError?>>? _compiledRules;

    /// <summary>
    /// Define a rule for a property using FluentValidation-compatible syntax.
    /// </summary>
    /// <typeparam name="TProperty">The type of the property being validated.</typeparam>
    /// <param name="expression">A lambda expression selecting the property to validate.</param>
    /// <returns>A <see cref="FluentRuleBuilder{T,TProperty}"/> for chaining validation methods.</returns>
    protected FluentRuleBuilder<T, TProperty> RuleFor<TProperty>(Expression<Func<T, TProperty?>> expression)
    {
        var builder = new FluentRuleBuilder<T, TProperty>(expression);
        _builders.Add(() => builder.Build());
        return builder;
    }

    /// <summary>
    /// Materializes builder delegates into the flat rule list on first use.
    /// Thread-safe via simple lock; validators are typically singletons so this
    /// initialization cost is amortized across all calls.
    /// </summary>
    private List<Func<T, ValidationError?>> GetCompiledRules()
    {
        if (_compiledRules is not null)
        {
            return _compiledRules;
        }

        var rules = new List<Func<T, ValidationError?>>();
        foreach (var builderFactory in _builders)
        {
            rules.AddRange(builderFactory());
        }

        _compiledRules = rules;
        return _compiledRules;
    }

    /// <summary>
    /// Validates the specified instance and returns a <see cref="GuardResult"/>.
    /// </summary>
    /// <param name="value">The object instance to validate.</param>
    /// <returns>
    /// A <see cref="GuardResult"/> that is successful if all rules pass,
    /// or contains accumulated <see cref="ValidationError"/> instances otherwise.
    /// </returns>
    public GuardResult Validate(T value)
    {
        var rules = GetCompiledRules();
        var errors = new List<ValidationError>();

        foreach (var rule in rules)
        {
            var error = rule(value);
            if (error is not null)
            {
                errors.Add(error);
            }
        }

        return errors.Count == 0 ? GuardResult.Success() : GuardResult.Failure(errors);
    }

    /// <summary>
    /// Validates the specified instance asynchronously.
    /// </summary>
    /// <param name="value">The object instance to validate.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>
    /// A <see cref="GuardResult"/> that is successful if all rules pass,
    /// or contains accumulated <see cref="ValidationError"/> instances otherwise.
    /// </returns>
    public Task<GuardResult> ValidateAsync(T value, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Validate(value));
    }
}
