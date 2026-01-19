namespace Moongazing.OrionGuard.Core;

/// <summary>
/// Provides async validation support for values that require asynchronous checks.
/// </summary>
public sealed class AsyncGuard<T>
{
    private readonly T _value;
    private readonly string _parameterName;
    private readonly List<ValidationError> _errors;
    private readonly List<Func<Task<ValidationError?>>> _asyncValidations;
    private bool _shouldValidate = true;

    internal AsyncGuard(T value, string parameterName)
    {
        _value = value;
        _parameterName = parameterName;
        _errors = new List<ValidationError>();
        _asyncValidations = new List<Func<Task<ValidationError?>>>();
    }

    /// <summary>
    /// Gets the value being validated.
    /// </summary>
    public T Value => _value;

    /// <summary>
    /// Conditional validation - only applies when condition is true.
    /// </summary>
    public AsyncGuard<T> When(bool condition)
    {
        _shouldValidate = condition;
        return this;
    }

    /// <summary>
    /// Adds an async validation predicate.
    /// </summary>
    public AsyncGuard<T> MustAsync(Func<T, Task<bool>> predicate, string message, string? errorCode = null)
    {
        if (!_shouldValidate) return this;

        _asyncValidations.Add(async () =>
        {
            var isValid = await predicate(_value).ConfigureAwait(false);
            return isValid ? null : new ValidationError(_parameterName, message, errorCode ?? "ASYNC_CUSTOM");
        });

        return this;
    }

    /// <summary>
    /// Validates entity exists (e.g., in database).
    /// </summary>
    public AsyncGuard<T> ExistsAsync(Func<T, Task<bool>> existsCheck, string? message = null)
    {
        return MustAsync(existsCheck, message ?? $"{_parameterName} does not exist.", "EXISTS");
    }

    /// <summary>
    /// Validates entity is unique (e.g., email not already registered).
    /// </summary>
    public AsyncGuard<T> UniqueAsync(Func<T, Task<bool>> uniqueCheck, string? message = null)
    {
        return MustAsync(uniqueCheck, message ?? $"{_parameterName} must be unique.", "UNIQUE");
    }

    /// <summary>
    /// Executes all validations and returns the result.
    /// </summary>
    public async Task<GuardResult> ValidateAsync()
    {
        foreach (var validation in _asyncValidations)
        {
            var error = await validation().ConfigureAwait(false);
            if (error != null)
            {
                _errors.Add(error);
            }
        }

        return _errors.Count == 0 ? GuardResult.Success() : GuardResult.Failure(_errors);
    }

    /// <summary>
    /// Executes validations and returns the value if valid, throws otherwise.
    /// </summary>
    public async Task<T> ValidateAndGetAsync()
    {
        var result = await ValidateAsync().ConfigureAwait(false);
        result.ThrowIfInvalid();
        return _value;
    }

    /// <summary>
    /// Tries to validate and returns success status with the value.
    /// </summary>
    public async Task<(bool IsValid, T Value, IReadOnlyList<ValidationError> Errors)> TryValidateAsync()
    {
        var result = await ValidateAsync().ConfigureAwait(false);
        return (result.IsValid, _value, result.Errors);
    }
}

/// <summary>
/// Entry point for async validations.
/// </summary>
public static class EnsureAsync
{
    /// <summary>
    /// Creates an async guard for the specified value.
    /// </summary>
    public static AsyncGuard<T> That<T>(T value, string parameterName)
    {
        return new AsyncGuard<T>(value, parameterName);
    }

    /// <summary>
    /// Combines multiple async validation results.
    /// </summary>
    public static async Task<GuardResult> AllAsync(params Task<GuardResult>[] validations)
    {
        var results = await Task.WhenAll(validations).ConfigureAwait(false);
        return GuardResult.Combine(results);
    }
}
