using Microsoft.Extensions.DependencyInjection;

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
/// Base class for creating validators with fluent API.
/// </summary>
public abstract class AbstractValidator<T> : IValidator<T>
{
    private readonly List<Func<T, Core.ValidationError?>> _rules = new();
    private readonly List<Func<T, Task<Core.ValidationError?>>> _asyncRules = new();

    /// <summary>
    /// Adds a validation rule.
    /// </summary>
    protected void RuleFor<TProperty>(
        Func<T, TProperty> selector,
        string propertyName,
        Action<PropertyValidator<TProperty>> configure)
    {
        var propertyValidator = new PropertyValidator<TProperty>(propertyName);
        configure(propertyValidator);

        _rules.Add(value =>
        {
            var propertyValue = selector(value);
            return propertyValidator.Validate(propertyValue);
        });
    }

    /// <summary>
    /// Adds a custom validation rule.
    /// </summary>
    protected void RuleFor(Func<T, bool> predicate, string message, string propertyName)
    {
        _rules.Add(value =>
        {
            if (!predicate(value))
            {
                return new Core.ValidationError(propertyName, message);
            }
            return null;
        });
    }

    /// <summary>
    /// Adds an async validation rule.
    /// </summary>
    protected void RuleForAsync(Func<T, Task<bool>> predicate, string message, string propertyName)
    {
        _asyncRules.Add(async value =>
        {
            if (!await predicate(value).ConfigureAwait(false))
            {
                return new Core.ValidationError(propertyName, message);
            }
            return null;
        });
    }

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
