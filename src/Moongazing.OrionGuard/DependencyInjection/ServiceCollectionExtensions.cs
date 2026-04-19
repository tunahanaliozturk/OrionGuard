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

    /// <summary>
    /// Validates the specified value with a <see cref="Core.ValidationContext"/> that rules
    /// can consult for contextual data (tenant id, user role, feature flags, etc.).
    /// </summary>
    /// <remarks>
    /// Default interface implementation delegates to the context-less overload so existing
    /// implementations remain source-compatible. Implementations that want to honor the
    /// context should override this method.
    /// </remarks>
    Core.GuardResult Validate(T value, Core.ValidationContext context) => Validate(value);

    /// <summary>
    /// Asynchronously validates the specified value with a <see cref="Core.ValidationContext"/>.
    /// </summary>
    Task<Core.GuardResult> ValidateAsync(T value, Core.ValidationContext context, CancellationToken cancellationToken = default)
        => ValidateAsync(value, cancellationToken);
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
    // Single source of truth: rule-set name -> typed rule lists.
    // Keeps rules as Func<T, ...> so no object/T cast closures are allocated per rule.
    private readonly Dictionary<string, TypedRuleSet> _ruleSets =
        new(StringComparer.OrdinalIgnoreCase);

    private string? _currentRuleSet;

    private sealed class TypedRuleSet
    {
        public List<RuleEntry> Sync { get; } = new();
        public List<AsyncRuleEntry> Async { get; } = new();
    }

    /// <summary>
    /// A sync rule: the predicate returns a <see cref="Core.ValidationError"/> when failing,
    /// or <c>null</c> when passing. The <see cref="RuleBuilder"/> post-processes the returned
    /// error to apply severity/error-code overrides.
    /// </summary>
    private sealed class RuleEntry
    {
        public Func<T, Core.ValidationContext, Core.ValidationError?> Predicate { get; }
        public RuleBuilder Builder { get; }
        public RuleEntry(
            Func<T, Core.ValidationContext, Core.ValidationError?> predicate,
            RuleBuilder builder)
        {
            Predicate = predicate;
            Builder = builder;
        }

        public Core.ValidationError? Invoke(T value, Core.ValidationContext ctx) =>
            Builder.Apply(Predicate(value, ctx));
    }

    private sealed class AsyncRuleEntry
    {
        public Func<T, Core.ValidationContext, Task<Core.ValidationError?>> Predicate { get; }
        public RuleBuilder Builder { get; }
        public AsyncRuleEntry(
            Func<T, Core.ValidationContext, Task<Core.ValidationError?>> predicate,
            RuleBuilder builder)
        {
            Predicate = predicate;
            Builder = builder;
        }

        /// <summary>
        /// Invokes the predicate and never propagates exceptions. Unexpected throws are
        /// converted into a <see cref="Core.ValidationError"/> with error code
        /// <c>RULE_EXECUTION_FAILED</c>. Rationale: validators should collect <i>all</i> errors
        /// in a single pass -- a throw from one rule must not short-circuit sibling rules
        /// running concurrently, nor lose their errors via <see cref="Task.WhenAll(Task[])"/>
        /// exception unwrapping. <see cref="OperationCanceledException"/> is the one
        /// exception: it is allowed to propagate so cooperative cancellation still works.
        /// </summary>
        public async Task<Core.ValidationError?> InvokeAsync(T value, Core.ValidationContext ctx)
        {
            Core.ValidationError? raw;
            try
            {
                raw = await Predicate(value, ctx).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Exception observed here; surface as a first-class validation error so
                // callers see it alongside other errors collected in the same run.
                raw = new Core.ValidationError(
                    ParameterName: "",
                    Message: $"Async validation rule threw: {ex.GetType().Name}: {ex.Message}",
                    ErrorCode: "RULE_EXECUTION_FAILED");
            }
            return Builder.Apply(raw);
        }
    }

    /// <summary>
    /// Mutable per-rule metadata. Returned from <c>RuleFor</c> so callers can chain
    /// modifiers. The builder is captured by its <see cref="RuleEntry"/> so that any
    /// overrides applied later are picked up when the rule runs.
    /// </summary>
    private sealed class RuleBuilder : Core.IRuleBuilder
    {
        public Core.Severity Severity { get; private set; } = Core.Severity.Error;
        public string? ErrorCode { get; private set; }
        public bool IsParallel { get; private set; }

        public Core.IRuleBuilder WithSeverity(Core.Severity severity)
        {
            Severity = severity;
            return this;
        }

        public Core.IRuleBuilder WithErrorCode(string errorCode)
        {
            ArgumentException.ThrowIfNullOrEmpty(errorCode);
            ErrorCode = errorCode;
            return this;
        }

        public Core.IRuleBuilder Parallel()
        {
            IsParallel = true;
            return this;
        }

        /// <summary>
        /// Applies the builder's overrides to a raw <see cref="Core.ValidationError"/>.
        /// When the predicate produced no error, returns <c>null</c> unchanged.
        /// </summary>
        public Core.ValidationError? Apply(Core.ValidationError? error)
        {
            if (error is null) return null;
            return error with
            {
                Severity = Severity,
                ErrorCode = ErrorCode ?? error.ErrorCode
            };
        }
    }

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

    private TypedRuleSet GetOrCreateRuleSet(string name)
    {
        if (!_ruleSets.TryGetValue(name, out var set))
        {
            set = new TypedRuleSet();
            _ruleSets[name] = set;
        }
        return set;
    }

    /// <summary>
    /// Returns the name of the rule set that new rules should be added to.
    /// Falls back to <see cref="Core.RuleSet.Default"/> when no explicit group is active.
    /// </summary>
    private string ActiveRuleSetName => _currentRuleSet ?? Core.RuleSet.Default;

    /// <summary>
    /// Adds a validation rule for a property using the fluent property validator.
    /// </summary>
    /// <returns>
    /// An <see cref="Core.IRuleBuilder"/> for chaining modifiers like
    /// <see cref="Core.IRuleBuilder.WithSeverity(Core.Severity)"/> or
    /// <see cref="Core.IRuleBuilder.WithErrorCode(string)"/>.
    /// </returns>
    protected Core.IRuleBuilder RuleFor<TProperty>(
        Func<T, TProperty> selector,
        string propertyName,
        Action<PropertyValidator<TProperty>> configure)
    {
        var propertyValidator = new PropertyValidator<TProperty>(propertyName);
        configure(propertyValidator);

        return AddSyncRule((value, _) => propertyValidator.Validate(selector(value)));
    }

    /// <summary>
    /// Adds a custom validation rule using a predicate.
    /// </summary>
    protected Core.IRuleBuilder RuleFor(Func<T, bool> predicate, string message, string propertyName)
    {
        return AddSyncRule((value, _) =>
            predicate(value) ? null : new Core.ValidationError(propertyName, message));
    }

    /// <summary>
    /// Adds a custom validation rule that can consult the <see cref="Core.ValidationContext"/>.
    /// Useful for authorization-aware rules, tenant-scoped checks, or feature-flag-gated logic.
    /// </summary>
    protected Core.IRuleBuilder RuleFor(
        Func<T, Core.ValidationContext, bool> predicate,
        string message,
        string propertyName)
    {
        return AddSyncRule((value, ctx) =>
            predicate(value, ctx) ? null : new Core.ValidationError(propertyName, message));
    }

    /// <summary>
    /// Adds an async validation rule using an async predicate.
    /// </summary>
    protected Core.IRuleBuilder RuleForAsync(Func<T, Task<bool>> predicate, string message, string propertyName)
    {
        return AddAsyncRule(async (value, _) =>
            await predicate(value).ConfigureAwait(false)
                ? null
                : new Core.ValidationError(propertyName, message));
    }

    /// <summary>
    /// Adds an async validation rule that can consult the <see cref="Core.ValidationContext"/>.
    /// </summary>
    protected Core.IRuleBuilder RuleForAsync(
        Func<T, Core.ValidationContext, Task<bool>> predicate,
        string message,
        string propertyName)
    {
        return AddAsyncRule(async (value, ctx) =>
            await predicate(value, ctx).ConfigureAwait(false)
                ? null
                : new Core.ValidationError(propertyName, message));
    }

    private RuleBuilder AddSyncRule(Func<T, Core.ValidationContext, Core.ValidationError?> predicate)
    {
        var builder = new RuleBuilder();
        GetOrCreateRuleSet(ActiveRuleSetName).Sync.Add(new RuleEntry(predicate, builder));
        return builder;
    }

    private RuleBuilder AddAsyncRule(Func<T, Core.ValidationContext, Task<Core.ValidationError?>> predicate)
    {
        var builder = new RuleBuilder();
        GetOrCreateRuleSet(ActiveRuleSetName).Async.Add(new AsyncRuleEntry(predicate, builder));
        return builder;
    }

    /// <summary>
    /// Validates the value by running <b>all</b> rule sets (default + every named group)
    /// using <see cref="Core.ValidationContext.Empty"/>.
    /// </summary>
    public Core.GuardResult Validate(T value) => Validate(value, Core.ValidationContext.Empty);

    /// <summary>
    /// Validates the value with a context by running <b>all</b> rule sets.
    /// </summary>
    /// <param name="value">The object to validate.</param>
    /// <param name="context">Contextual data rules may consult (tenant id, user role, etc.).</param>
    public Core.GuardResult Validate(T value, Core.ValidationContext context)
    {
        List<Core.ValidationError>? errors = null;

        foreach (var set in _ruleSets.Values)
        {
            foreach (var rule in set.Sync)
            {
                var error = rule.Invoke(value, context);
                if (error is not null)
                {
                    (errors ??= new()).Add(error);
                }
            }
        }

        return errors is null ? Core.GuardResult.Success() : Core.GuardResult.Failure(errors);
    }

    /// <summary>
    /// Validates the value by running only the specified rule sets.
    /// </summary>
    public Core.GuardResult Validate(T value, params string[] ruleSets) =>
        Validate(value, Core.ValidationContext.Empty, ruleSets);

    /// <summary>
    /// Validates the value with a context by running only the specified rule sets.
    /// </summary>
    public Core.GuardResult Validate(T value, Core.ValidationContext context, params string[] ruleSets)
    {
        if (ruleSets is null || ruleSets.Length == 0)
        {
            return Validate(value, context);
        }

        List<Core.ValidationError>? errors = null;

        foreach (var name in ruleSets)
        {
            if (!_ruleSets.TryGetValue(name, out var set)) continue;

            foreach (var rule in set.Sync)
            {
                var error = rule.Invoke(value, context);
                if (error is not null)
                {
                    (errors ??= new()).Add(error);
                }
            }
        }

        return errors is null ? Core.GuardResult.Success() : Core.GuardResult.Failure(errors);
    }

    /// <summary>
    /// Validates the value asynchronously by running <b>all</b> rule sets.
    /// </summary>
    public Task<Core.GuardResult> ValidateAsync(T value, CancellationToken cancellationToken = default) =>
        ValidateAsync(value, Core.ValidationContext.Empty, cancellationToken);

    /// <summary>
    /// Validates the value asynchronously with a context by running <b>all</b> rule sets.
    /// Async rules marked <see cref="Core.IRuleBuilder.Parallel"/> are batched and awaited
    /// concurrently via <see cref="Task.WhenAll(Task[])"/>.
    /// </summary>
    public async Task<Core.GuardResult> ValidateAsync(
        T value, Core.ValidationContext context, CancellationToken cancellationToken = default)
    {
        List<Core.ValidationError>? errors = null;

        foreach (var set in _ruleSets.Values)
        {
            RunSyncRules(set.Sync, value, context, ref errors);
            await RunAsyncRules(set.Async, value, context, e =>
            {
                (errors ??= new()).Add(e);
            }, cancellationToken).ConfigureAwait(false);
        }

        return errors is null ? Core.GuardResult.Success() : Core.GuardResult.Failure(errors);
    }

    /// <summary>
    /// Validates the value asynchronously by running only the specified rule sets.
    /// </summary>
    public Task<Core.GuardResult> ValidateAsync(T value, CancellationToken cancellationToken, params string[] ruleSets) =>
        ValidateAsync(value, Core.ValidationContext.Empty, cancellationToken, ruleSets);

    /// <summary>
    /// Validates the value asynchronously with a context by running only the specified rule sets.
    /// </summary>
    public async Task<Core.GuardResult> ValidateAsync(
        T value,
        Core.ValidationContext context,
        CancellationToken cancellationToken,
        params string[] ruleSets)
    {
        if (ruleSets is null || ruleSets.Length == 0)
        {
            return await ValidateAsync(value, context, cancellationToken).ConfigureAwait(false);
        }

        List<Core.ValidationError>? errors = null;

        foreach (var name in ruleSets)
        {
            if (!_ruleSets.TryGetValue(name, out var set)) continue;

            RunSyncRules(set.Sync, value, context, ref errors);
            await RunAsyncRules(set.Async, value, context, e =>
            {
                (errors ??= new()).Add(e);
            }, cancellationToken).ConfigureAwait(false);
        }

        return errors is null ? Core.GuardResult.Success() : Core.GuardResult.Failure(errors);
    }

    private static void RunSyncRules(
        List<RuleEntry> rules,
        T value,
        Core.ValidationContext context,
        ref List<Core.ValidationError>? errors)
    {
        foreach (var rule in rules)
        {
            var error = rule.Invoke(value, context);
            if (error is not null)
            {
                (errors ??= new()).Add(error);
            }
        }
    }

    /// <summary>
    /// Runs async rules with parallel-batching semantics: consecutive rules whose builder
    /// is flagged <see cref="RuleBuilder.IsParallel"/> are awaited concurrently via
    /// <see cref="Task.WhenAll(Task[])"/>. A non-parallel rule flushes any pending batch
    /// and runs sequentially. Preserves definition-order side effects for sequential rules
    /// while unlocking parallelism where the author opted in.
    /// </summary>
    private static async Task RunAsyncRules(
        List<AsyncRuleEntry> rules,
        T value,
        Core.ValidationContext context,
        Action<Core.ValidationError> emit,
        CancellationToken cancellationToken)
    {
        List<Task<Core.ValidationError?>>? batch = null;

        foreach (var rule in rules)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (rule.Builder.IsParallel)
            {
                (batch ??= new()).Add(rule.InvokeAsync(value, context));
                continue;
            }

            // Flush any pending parallel batch before running the sequential rule.
            if (batch is { Count: > 0 })
            {
                await FlushBatch(batch, emit).ConfigureAwait(false);
                batch.Clear();
            }

            var error = await rule.InvokeAsync(value, context).ConfigureAwait(false);
            if (error is not null) emit(error);
        }

        if (batch is { Count: > 0 })
        {
            await FlushBatch(batch, emit).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Awaits a batch of concurrent rule tasks and emits any non-null results.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Exception safety.</b> By contract, <see cref="AsyncRuleEntry.InvokeAsync"/>
    /// only ever throws <see cref="OperationCanceledException"/> -- every other exception
    /// is converted into a <see cref="Core.ValidationError"/> before the task completes.
    /// This sidesteps <see cref="Task.WhenAll(Task[])"/>'s most dangerous behaviour:
    /// <see cref="AggregateException"/> unwrapping that surfaces only the first fault and
    /// silently discards the rest. Here, a misbehaving rule adds one error; siblings keep
    /// going; nothing is lost.
    /// </para>
    /// <para>
    /// <b>Cancellation.</b> If any task is canceled, <see cref="Task.WhenAll(Task[])"/>
    /// throws <see cref="TaskCanceledException"/> and propagates upward -- the outer
    /// <c>ValidateAsync</c> caller observes a clean cancellation boundary.
    /// </para>
    /// </remarks>
    private static async Task FlushBatch(
        List<Task<Core.ValidationError?>> batch,
        Action<Core.ValidationError> emit)
    {
        var results = await Task.WhenAll(batch).ConfigureAwait(false);
        foreach (var error in results)
        {
            if (error is not null) emit(error);
        }
    }

    /// <summary>
    /// Gets the names of all registered rule sets.
    /// </summary>
    public IReadOnlyCollection<string> GetRuleSetNames() => _ruleSets.Keys.ToList().AsReadOnly();

    /// <summary>
    /// Imports all rules from another <see cref="AbstractValidator{T}"/> of the same type.
    /// Rules from the included validator preserve their original rule-set grouping;
    /// rules that belonged to <c>"default"</c> in the child validator become part of the
    /// current validator's <c>"default"</c> set as well.
    /// </summary>
    /// <param name="other">The validator whose rules should be imported.</param>
    /// <remarks>
    /// <para>
    /// <b>Use case.</b> Share common rules (e.g., a base <c>UserValidator</c> with email and
    /// password rules) across multiple operation-specific validators (<c>CreateUserValidator</c>,
    /// <c>UpdateUserValidator</c>) without re-declaring them.
    /// </para>
    /// <para>
    /// <b>Semantics.</b> Rules are imported <i>by reference</i> -- they share the same
    /// <see cref="RuleBuilder"/> as in the source validator. Modifying severity on the
    /// child after <c>Include</c> is called will also affect the parent. If you need
    /// independent copies, clone your rules manually.
    /// </para>
    /// <para>
    /// <b>Ordering.</b> Imported rules run <i>after</i> rules declared directly in this
    /// validator, in the order they were defined in the source. This makes composition
    /// behave like standard inheritance: local rules first, base rules second.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is null.</exception>
    protected void Include(AbstractValidator<T> other)
    {
        ArgumentNullException.ThrowIfNull(other);

        foreach (var (name, source) in other._ruleSets)
        {
            var target = GetOrCreateRuleSet(name);
            target.Sync.AddRange(source.Sync);
            target.Async.AddRange(source.Async);
        }
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
