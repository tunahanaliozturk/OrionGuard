using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moongazing.OrionGuard.Core;

namespace Moongazing.OrionGuard.DependencyInjection;

/// <summary>
/// Extension methods for registering OrionGuard services with DI container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds OrionGuard validation services to the DI container.
    /// </summary>
    public static IServiceCollection AddOrionGuard(this IServiceCollection services)
    {
        services.TryAddSingleton<IExceptionFactory>(DefaultExceptionFactory.Instance);
        services.AddSingleton<IValidatorFactory, ValidatorFactory>();
        return services;
    }

    /// <summary>
    /// Adds OrionGuard with custom validator registration.
    /// </summary>
    public static IServiceCollection AddOrionGuard(this IServiceCollection services, Action<ValidatorRegistry> configure)
    {
        var registry = new ValidatorRegistry();
        configure(registry);
        services.AddSingleton(registry);
        services.AddSingleton<IValidatorFactory, ValidatorFactory>();
        return services;
    }

    /// <summary>
    /// Registers a custom exception factory for OrionGuard.
    /// The factory is instantiated immediately and wired into both the DI container
    /// and the static <see cref="ExceptionFactoryProvider"/> so that non-DI code paths
    /// (e.g. Guard.Against helpers) also use the custom factory.
    /// </summary>
    public static IServiceCollection AddOrionGuardExceptionFactory<TFactory>(this IServiceCollection services)
        where TFactory : class, IExceptionFactory, new()
    {
        var factory = new TFactory();
        ExceptionFactoryProvider.Configure(factory);
        services.AddSingleton<IExceptionFactory>(factory);
        return services;
    }

    /// <summary>
    /// Registers a validator for a specific type.
    /// </summary>
    public static IServiceCollection AddValidator<T, TValidator>(this IServiceCollection services)
        where TValidator : class, IValidator<T>
    {
        services.AddTransient<IValidator<T>, TValidator>();
        return services;
    }
}

/// <summary>
/// Factory interface for creating validators.
/// </summary>
public interface IValidatorFactory
{
    /// <summary>
    /// Gets a validator for the specified type.
    /// </summary>
    IValidator<T>? GetValidator<T>();
}

/// <summary>
/// Default implementation of validator factory.
/// </summary>
public sealed class ValidatorFactory : IValidatorFactory
{
    private readonly IServiceProvider _serviceProvider;

    public ValidatorFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public IValidator<T>? GetValidator<T>()
    {
        return _serviceProvider.GetService<IValidator<T>>();
    }
}

/// <summary>
/// Registry for custom validators.
/// </summary>
public sealed class ValidatorRegistry
{
    private readonly Dictionary<Type, Type> _validators = new();

    /// <summary>
    /// Registers a validator for a type.
    /// </summary>
    public ValidatorRegistry Register<T, TValidator>() where TValidator : IValidator<T>
    {
        _validators[typeof(T)] = typeof(TValidator);
        return this;
    }

    internal Type? GetValidatorType(Type modelType)
    {
        return _validators.TryGetValue(modelType, out var validatorType) ? validatorType : null;
    }
}

/// <summary>
/// Interface for type-specific validators.
/// </summary>
public interface IValidator<T>
{
    /// <summary>
    /// Validates the specified value and returns a result.
    /// </summary>
    Core.GuardResult Validate(T value);

    /// <summary>
    /// Validates the specified value asynchronously.
    /// </summary>
    Task<Core.GuardResult> ValidateAsync(T value, CancellationToken cancellationToken = default);
}

/// <summary>
/// Base class for creating validators with fluent API and rule set support.
/// <para>
/// Rules defined outside of a <see cref="RuleSet(string, Action)"/> block are placed in the
/// <see cref="Core.RuleSet.Default"/> group. The parameterless <see cref="Validate(T)"/> overload
/// executes <b>all</b> rule sets. Use <see cref="Validate(T, string[])"/> to run only specific groups.
/// </para>
/// </summary>
public abstract class AbstractValidator<T> : IValidator<T>
{
    private readonly Dictionary<string, Core.RuleSet> _ruleSets = new(StringComparer.OrdinalIgnoreCase);
    private string? _currentRuleSet;

    // Legacy flat lists kept as views into the default rule set for backward compatibility
    // with any subclass that may have referenced them via reflection (defensive).
    private readonly List<Func<T, Core.ValidationError?>> _rules = new();
    private readonly List<Func<T, Task<Core.ValidationError?>>> _asyncRules = new();

    /// <summary>
    /// Defines a named rule set. All <c>RuleFor</c> / <c>RuleForAsync</c> calls inside
    /// <paramref name="configure"/> are associated with that group.
    /// </summary>
    /// <param name="name">
    /// The rule set name. Use <see cref="Core.RuleSet"/> constants for well-known names
    /// (e.g., <see cref="Core.RuleSet.Create"/>, <see cref="Core.RuleSet.Update"/>).
    /// </param>
    /// <param name="configure">Action that registers rules belonging to this group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="name"/> or <paramref name="configure"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when rule sets are nested.</exception>
    protected void RuleSet(string name, Action configure)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(configure);

        if (_currentRuleSet is not null)
        {
            throw new InvalidOperationException(
                $"Cannot nest rule sets. Already inside rule set '{_currentRuleSet}'.");
        }

        _currentRuleSet = name;
        try
        {
            configure();
        }
        finally
        {
            _currentRuleSet = null;
        }
    }

    /// <summary>
    /// Gets or creates the <see cref="Core.RuleSet"/> for the given name.
    /// </summary>
    private Core.RuleSet GetOrCreateRuleSet(string name)
    {
        if (!_ruleSets.TryGetValue(name, out var ruleSet))
        {
            ruleSet = new Core.RuleSet(name);
            _ruleSets[name] = ruleSet;
        }
        return ruleSet;
    }

    /// <summary>
    /// Returns the name of the rule set that new rules should be added to.
    /// Falls back to <see cref="Core.RuleSet.Default"/> when no explicit group is active.
    /// </summary>
    private string ActiveRuleSetName => _currentRuleSet ?? Core.RuleSet.Default;

    /// <summary>
    /// Adds a validation rule for a property using the fluent property validator.
    /// </summary>
    protected void RuleFor<TProperty>(
        Func<T, TProperty> selector,
        string propertyName,
        Action<PropertyValidator<TProperty>> configure)
    {
        var propertyValidator = new PropertyValidator<TProperty>(propertyName);
        configure(propertyValidator);

        Func<T, Core.ValidationError?> rule = value =>
        {
            var propertyValue = selector(value);
            return propertyValidator.Validate(propertyValue);
        };

        // Add to the active rule set
        var ruleSet = GetOrCreateRuleSet(ActiveRuleSetName);
        ruleSet.Rules.Add(obj => rule((T)obj));

        // Also keep in flat list for backward compatibility
        _rules.Add(rule);
    }

    /// <summary>
    /// Adds a custom validation rule using a predicate.
    /// </summary>
    protected void RuleFor(Func<T, bool> predicate, string message, string propertyName)
    {
        Func<T, Core.ValidationError?> rule = value =>
        {
            if (!predicate(value))
            {
                return new Core.ValidationError(propertyName, message);
            }
            return null;
        };

        var ruleSet = GetOrCreateRuleSet(ActiveRuleSetName);
        ruleSet.Rules.Add(obj => rule((T)obj));

        _rules.Add(rule);
    }

    /// <summary>
    /// Adds an async validation rule using an async predicate.
    /// </summary>
    protected void RuleForAsync(Func<T, Task<bool>> predicate, string message, string propertyName)
    {
        Func<T, Task<Core.ValidationError?>> rule = async value =>
        {
            if (!await predicate(value).ConfigureAwait(false))
            {
                return new Core.ValidationError(propertyName, message);
            }
            return null;
        };

        var ruleSet = GetOrCreateRuleSet(ActiveRuleSetName);
        ruleSet.AsyncRules.Add(async obj => await rule((T)obj).ConfigureAwait(false));

        _asyncRules.Add(rule);
    }

    /// <summary>
    /// Validates the value by running <b>all</b> rule sets (default + every named group).
    /// This preserves full backward compatibility with the original parameterless overload.
    /// </summary>
    public Core.GuardResult Validate(T value)
    {
        var errors = new List<Core.ValidationError>();

        foreach (var rule in _rules)
        {
            var error = rule(value);
            if (error != null)
            {
                errors.Add(error);
            }
        }

        return errors.Count == 0 ? Core.GuardResult.Success() : Core.GuardResult.Failure(errors);
    }

    /// <summary>
    /// Validates the value by running only the specified rule sets.
    /// </summary>
    /// <param name="value">The object to validate.</param>
    /// <param name="ruleSets">
    /// One or more rule set names to execute. Use <see cref="Core.RuleSet"/> constants
    /// for well-known names. If a requested rule set does not exist, it is silently skipped.
    /// </param>
    /// <returns>A <see cref="Core.GuardResult"/> containing any validation errors found.</returns>
    public Core.GuardResult Validate(T value, params string[] ruleSets)
    {
        if (ruleSets is null || ruleSets.Length == 0)
        {
            return Validate(value);
        }

        var errors = new List<Core.ValidationError>();

        foreach (var ruleSetName in ruleSets)
        {
            if (_ruleSets.TryGetValue(ruleSetName, out var ruleSet))
            {
                foreach (var rule in ruleSet.Rules)
                {
                    var error = rule(value!);
                    if (error != null)
                    {
                        errors.Add(error);
                    }
                }
            }
        }

        return errors.Count == 0 ? Core.GuardResult.Success() : Core.GuardResult.Failure(errors);
    }

    /// <summary>
    /// Validates the value asynchronously by running <b>all</b> rule sets.
    /// </summary>
    public async Task<Core.GuardResult> ValidateAsync(T value, CancellationToken cancellationToken = default)
    {
        var errors = new List<Core.ValidationError>();

        // Run sync rules first
        foreach (var rule in _rules)
        {
            var error = rule(value);
            if (error != null)
            {
                errors.Add(error);
            }
        }

        // Then async rules
        foreach (var rule in _asyncRules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var error = await rule(value).ConfigureAwait(false);
            if (error != null)
            {
                errors.Add(error);
            }
        }

        return errors.Count == 0 ? Core.GuardResult.Success() : Core.GuardResult.Failure(errors);
    }

    /// <summary>
    /// Validates the value asynchronously by running only the specified rule sets.
    /// </summary>
    /// <param name="value">The object to validate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="ruleSets">
    /// One or more rule set names to execute. If empty or null, all rule sets run.
    /// </param>
    /// <returns>A <see cref="Core.GuardResult"/> containing any validation errors found.</returns>
    public async Task<Core.GuardResult> ValidateAsync(T value, CancellationToken cancellationToken, params string[] ruleSets)
    {
        if (ruleSets is null || ruleSets.Length == 0)
        {
            return await ValidateAsync(value, cancellationToken).ConfigureAwait(false);
        }

        var errors = new List<Core.ValidationError>();

        foreach (var ruleSetName in ruleSets)
        {
            if (_ruleSets.TryGetValue(ruleSetName, out var ruleSet))
            {
                foreach (var rule in ruleSet.Rules)
                {
                    var error = rule(value!);
                    if (error != null)
                    {
                        errors.Add(error);
                    }
                }

                foreach (var rule in ruleSet.AsyncRules)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var error = await rule(value!).ConfigureAwait(false);
                    if (error != null)
                    {
                        errors.Add(error);
                    }
                }
            }
        }

        return errors.Count == 0 ? Core.GuardResult.Success() : Core.GuardResult.Failure(errors);
    }

    /// <summary>
    /// Gets the names of all registered rule sets.
    /// </summary>
    public IReadOnlyCollection<string> GetRuleSetNames() => _ruleSets.Keys.ToList().AsReadOnly();
}

/// <summary>
/// Validator for individual properties.
/// </summary>
public sealed class PropertyValidator<T>
{
    private readonly string _propertyName;
    private readonly List<Func<T, Core.ValidationError?>> _rules = new();

    public PropertyValidator(string propertyName)
    {
        _propertyName = propertyName;
    }

    public PropertyValidator<T> NotNull(string? message = null)
    {
        _rules.Add(value =>
        {
            if (value is null)
            {
                return new Core.ValidationError(_propertyName, message ?? $"{_propertyName} cannot be null.");
            }
            return null;
        });
        return this;
    }

    public PropertyValidator<T> NotEmpty(string? message = null)
    {
        _rules.Add(value =>
        {
            if (value is string str && string.IsNullOrWhiteSpace(str))
            {
                return new Core.ValidationError(_propertyName, message ?? $"{_propertyName} cannot be empty.");
            }
            return null;
        });
        return this;
    }

    public PropertyValidator<T> Must(Func<T, bool> predicate, string message)
    {
        _rules.Add(value =>
        {
            if (!predicate(value))
            {
                return new Core.ValidationError(_propertyName, message);
            }
            return null;
        });
        return this;
    }

    public PropertyValidator<T> Length(int min, int max, string? message = null)
    {
        _rules.Add(value =>
        {
            if (value is string str && (str.Length < min || str.Length > max))
            {
                return new Core.ValidationError(_propertyName, message ?? $"{_propertyName} must be between {min} and {max} characters.");
            }
            return null;
        });
        return this;
    }

    public PropertyValidator<T> Email(string? message = null)
    {
        _rules.Add(value =>
        {
            if (value is string str && !System.Text.RegularExpressions.Regex.IsMatch(str, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
            {
                return new Core.ValidationError(_propertyName, message ?? $"{_propertyName} must be a valid email.");
            }
            return null;
        });
        return this;
    }

    public PropertyValidator<T> GreaterThan<TValue>(TValue min, string? message = null) where TValue : IComparable<TValue>
    {
        _rules.Add(value =>
        {
            if (value is TValue comparable && comparable.CompareTo(min) <= 0)
            {
                return new Core.ValidationError(_propertyName, message ?? $"{_propertyName} must be greater than {min}.");
            }
            return null;
        });
        return this;
    }

    internal Core.ValidationError? Validate(T value)
    {
        foreach (var rule in _rules)
        {
            var error = rule(value);
            if (error != null)
            {
                return error;
            }
        }
        return null;
    }
}
