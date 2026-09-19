using System.Linq.Expressions;

namespace Moongazing.OrionGuard.Core;

/// <summary>
/// Object validator that validates all properties of an object at once.
/// </summary>
/// <remarks>
/// <b>Accessor caching.</b> Compiled property accessors are cached in <see cref="AccessorCache{T, TProperty}"/>,
/// keyed by the member (<c>o =&gt; o.Name</c>) or the full member path (<c>o =&gt; o.Customer.Name</c>), so two
/// selectors share an accessor only when they read the same members. Other selector shapes are compiled
/// per call.
/// </remarks>
public sealed class ObjectValidator<T> where T : class
{
    private readonly T _instance;
    private readonly List<ValidationError> _errors = new();
    private readonly bool _throwOnFirstError;

    /// <summary>
    /// Async rules deferred until <see cref="ToResultAsync"/> is awaited. Each entry, given a
    /// <see cref="CancellationToken"/>, produces a <see cref="ValidationError"/> when failing or
    /// <c>null</c> when passing. Sync rules cannot defer (they run eagerly inside the fluent call),
    /// so the two rule kinds are stored separately. At resolution time their failures are merged
    /// with the eagerly-collected sync <see cref="_errors"/> into one <see cref="GuardResult"/>
    /// without ever mutating <see cref="_errors"/>, keeping the async terminal idempotent.
    /// </summary>
    private List<Func<CancellationToken, Task<ValidationError?>>>? _asyncRules;

    /// <summary>
    /// Errors produced by the deferred async rules, captured the first time the async terminal runs
    /// to completion. On repeated <see cref="ToResultAsync"/> / <see cref="ThrowIfInvalidAsync"/>
    /// calls the cached outcome is returned verbatim, so the async rules neither re-execute (no
    /// repeated I/O side effects) nor re-append (no duplicate errors). This mirrors the synchronous
    /// <see cref="ToResult"/> contract, where rules run once during the fluent chain and the terminal
    /// is a pure read of <see cref="_errors"/>.
    /// </summary>
    private List<ValidationError>? _asyncErrors;

    internal ObjectValidator(T instance, bool throwOnFirstError)
    {
        ArgumentNullException.ThrowIfNull(instance);
        _instance = instance;
        _throwOnFirstError = throwOnFirstError;
    }

    /// <summary>
    /// True when at least one async rule has been registered and the result must therefore be
    /// resolved through <see cref="ToResultAsync"/> / <see cref="BuildAsync"/> /
    /// <see cref="ThrowIfInvalidAsync"/> rather than the synchronous terminals.
    /// </summary>
    private bool HasAsyncRules => _asyncRules is { Count: > 0 };

    /// <summary>
    /// Validates a specific property of the object.
    /// </summary>
    public ObjectValidator<T> Property<TProperty>(
        Expression<Func<T, TProperty>> selector,
        Action<FluentGuard<TProperty>> configure)
    {
        var propertyName = GetPropertyName(selector);
        var accessor = GetOrCompileAccessor(selector);
        return ValidateProperty(accessor(_instance), propertyName, configure);
    }

    /// <summary>
    /// Validates a property addressed by a plain delegate instead of an expression.
    /// </summary>
    /// <remarks>
    /// The expression overload is the ergonomic one, but the C# compiler rebuilds the expression tree at
    /// every call, which dominates the cost of validating a small DTO. A non-capturing
    /// <c>Func&lt;T, TProperty&gt;</c> is built once and cached by the compiler in a static field, so this
    /// overload is the allocation-free way to validate the same property on a hot path. The property name
    /// is explicit because there is no expression to read it from.
    /// </remarks>
    /// <param name="selector">Reads the property from the instance.</param>
    /// <param name="propertyName">The name reported on any <see cref="ValidationError"/>.</param>
    /// <param name="configure">Configures the rules to run against the property value.</param>
    public ObjectValidator<T> Property<TProperty>(
        Func<T, TProperty> selector,
        string propertyName,
        Action<FluentGuard<TProperty>> configure)
    {
        ArgumentNullException.ThrowIfNull(selector);
        return ValidateProperty(selector(_instance), propertyName, configure);
    }

    private ObjectValidator<T> ValidateProperty<TProperty>(
        TProperty value,
        string propertyName,
        Action<FluentGuard<TProperty>> configure)
    {
        var guard = new FluentGuard<TProperty>(value, propertyName, throwOnFirstError: false);
        configure(guard);

        // Read the guard's failures directly. Building a GuardResult here only to unpack it again cost a
        // result plus a filtered copy of its errors on every property, passing or failing; a FluentGuard
        // only ever emits Severity.Error, so the filter never removed anything.
        var produced = guard.CollectedErrors;
        if (produced is not null)
        {
            _errors.AddRange(produced);

            if (_throwOnFirstError)
            {
                throw new AggregateValidationException(_errors);
            }
        }

        return this;
    }

    /// <summary>
    /// Validates a property against another property (cross-property validation).
    /// </summary>
    public ObjectValidator<T> CrossProperty<TProp1, TProp2>(
        Expression<Func<T, TProp1>> selector1,
        Expression<Func<T, TProp2>> selector2,
        Func<TProp1, TProp2, bool> predicate,
        string message,
        string? errorCode = null)
    {
        var name1 = GetPropertyName(selector1);
        var name2 = GetPropertyName(selector2);
        var value1 = GetOrCompileAccessor(selector1)(_instance);
        var value2 = GetOrCompileAccessor(selector2)(_instance);

        if (!predicate(value1, value2))
        {
            _errors.Add(new ValidationError($"{name1},{name2}", message, errorCode ?? "CROSS_PROPERTY"));

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
    /// <remarks>
    /// The type parameter is constrained to <c>class?</c> rather than <c>class</c> so a nullable
    /// reference property (for example <c>string?</c>) can be passed without a CS8634/CS8621
    /// nullability-mismatch warning. Validating a nullable reference for null is the exact intent of
    /// this method. The change is source- and binary-compatible: the emitted IL constraint is
    /// unchanged and only the compile-time nullability annotation is relaxed.
    /// </remarks>
    public ObjectValidator<T> NotNull<TProperty>(
        Expression<Func<T, TProperty>> selector,
        string? message = null) where TProperty : class?
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
    /// Registers an asynchronous rule on a property: useful for checks that require I/O such as a
    /// database uniqueness lookup or a remote service call. The rule is deferred and executed when
    /// <see cref="ToResultAsync"/> (or <see cref="BuildAsync"/> / <see cref="ThrowIfInvalidAsync"/>)
    /// is awaited, and participates in the same failure aggregation and short-circuit semantics as
    /// the synchronous rules on this validator.
    /// </summary>
    /// <typeparam name="TProperty">The property type.</typeparam>
    /// <param name="selector">An expression selecting the property to validate.</param>
    /// <param name="predicate">
    /// An async predicate returning <c>true</c> when the value is valid. The
    /// <see cref="CancellationToken"/> flows from the awaiting <see cref="ToResultAsync"/> call.
    /// </param>
    /// <param name="message">The error message emitted when the predicate returns <c>false</c>.</param>
    /// <param name="errorCode">Optional error code; defaults to <c>ASYNC_PREDICATE</c>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="selector"/> or <paramref name="predicate"/> is null.</exception>
    public ObjectValidator<T> MustAsync<TProperty>(
        Expression<Func<T, TProperty>> selector,
        Func<TProperty, CancellationToken, Task<bool>> predicate,
        string message,
        string? errorCode = null)
    {
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(predicate);

        var propertyName = GetPropertyName(selector);
        var accessor = GetOrCompileAccessor(selector);

        AddAsyncRule(async ct =>
        {
            var value = accessor(_instance);
            var isValid = await predicate(value, ct).ConfigureAwait(false);
            return isValid
                ? null
                : new ValidationError(propertyName, message, errorCode ?? "ASYNC_PREDICATE");
        });

        return this;
    }

    /// <summary>
    /// Registers an asynchronous rule over the whole instance. Use this when the check is not tied
    /// to a single property (for example, a composite uniqueness check across several fields).
    /// </summary>
    /// <param name="predicate">
    /// An async predicate returning <c>true</c> when the instance is valid. The
    /// <see cref="CancellationToken"/> flows from the awaiting <see cref="ToResultAsync"/> call.
    /// </param>
    /// <param name="message">The error message emitted when the predicate returns <c>false</c>.</param>
    /// <param name="parameterName">The parameter/property name attached to the emitted error.</param>
    /// <param name="errorCode">Optional error code; defaults to <c>ASYNC_PREDICATE</c>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="predicate"/> is null.</exception>
    public ObjectValidator<T> MustAsync(
        Func<T, CancellationToken, Task<bool>> predicate,
        string message,
        string parameterName,
        string? errorCode = null)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        AddAsyncRule(async ct =>
        {
            var isValid = await predicate(_instance, ct).ConfigureAwait(false);
            return isValid
                ? null
                : new ValidationError(parameterName, message, errorCode ?? "ASYNC_PREDICATE");
        });

        return this;
    }

    /// <summary>
    /// Conditionally registers asynchronous rules. The <paramref name="configure"/> block runs only
    /// when <paramref name="condition"/> is <c>true</c>, mirroring the synchronous
    /// <see cref="When(bool, Action{ObjectValidator{T}})"/> overload.
    /// </summary>
    /// <param name="condition">When false, no rules inside <paramref name="configure"/> are registered.</param>
    /// <param name="configure">The block that registers rules.</param>
    public ObjectValidator<T> WhenAsync(bool condition, Action<ObjectValidator<T>> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        if (condition)
        {
            configure(this);
        }
        return this;
    }

    private void AddAsyncRule(Func<CancellationToken, Task<ValidationError?>> rule)
    {
        (_asyncRules ??= new()).Add(rule);
    }

    /// <summary>
    /// Conditionally applies validation rules.
    /// </summary>
    public ObjectValidator<T> When(bool condition, Action<ObjectValidator<T>> configure)
    {
        if (condition)
        {
            configure(this);
        }
        return this;
    }

    /// <summary>
    /// Returns the validation result.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when asynchronous rules are pending. Async rules cannot run on the synchronous path;
    /// call <see cref="ToResultAsync"/> instead so their results are not silently discarded.
    /// </exception>
    public GuardResult ToResult()
    {
        ThrowIfAsyncRulesPending();
        return _errors.Count == 0 ? GuardResult.Success() : GuardResult.Failure(_errors);
    }

    /// <summary>
    /// Throws if validation failed.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when asynchronous rules are pending; call <see cref="ThrowIfInvalidAsync"/> instead.
    /// </exception>
    public T ThrowIfInvalid()
    {
        ThrowIfAsyncRulesPending();
        if (_errors.Count > 0)
        {
            throw new AggregateValidationException(_errors);
        }
        return _instance;
    }

    /// <summary>
    /// Returns the validated object if valid.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when asynchronous rules are pending; call <see cref="BuildAsync"/> instead.
    /// </exception>
    public T Build() => ThrowIfInvalid();

    /// <summary>
    /// Runs every rule -- the synchronous rules already collected plus the deferred asynchronous
    /// rules -- and returns the merged <see cref="GuardResult"/>.
    /// </summary>
    /// <param name="cancellationToken">
    /// A token observed before and during each async rule. Cancellation is honored: an
    /// <see cref="OperationCanceledException"/> propagates to the caller rather than being recorded
    /// as a validation error.
    /// </param>
    /// <returns>A successful result when all rules pass, otherwise the accumulated errors.</returns>
    /// <remarks>
    /// <para>
    /// <b>Aggregation.</b> Synchronous errors collected during the fluent chain are surfaced first,
    /// then async rules run and append into the same list, so sync and async failures arrive in a
    /// single <see cref="GuardResult"/>.
    /// </para>
    /// <para>
    /// <b>Short-circuit parity.</b> A strict validator (<see cref="Validate.ForStrict{T}(T)"/>)
    /// throws <see cref="AggregateValidationException"/> on the first failure -- including the first
    /// async failure, after which no further async rules run -- exactly matching the synchronous
    /// strict path. A non-strict validator (<see cref="Validate.For{T}(T)"/>) runs every rule and
    /// aggregates all failures.
    /// </para>
    /// </remarks>
    public async Task<GuardResult> ToResultAsync(CancellationToken cancellationToken = default)
    {
        // Strict mode: any sync error already collected would have thrown at registration time, so
        // reaching here in strict mode means the sync rules passed. Defensive parity all the same.
        if (_throwOnFirstError && _errors.Count > 0)
        {
            throw new AggregateValidationException(_errors);
        }

        // Idempotency: build the merged result from a FRESH local list every call and never mutate
        // the shared _errors. The async rules run exactly once -- their outcome is cached in
        // _asyncErrors -- so a second ToResultAsync()/ThrowIfInvalidAsync() returns the identical
        // error set without re-running the predicates (no repeated I/O side effects) and without
        // re-appending (no duplicate errors). This matches the synchronous ToResult() contract.
        if (_asyncErrors is null)
        {
            var produced = new List<ValidationError>();

            if (_asyncRules is not null)
            {
                foreach (var rule in _asyncRules)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var error = await rule(cancellationToken).ConfigureAwait(false);
                    if (error is null)
                    {
                        continue;
                    }

                    produced.Add(error);

                    if (_throwOnFirstError)
                    {
                        // Strict short-circuit: throw on first async failure, exactly as the sync
                        // strict path does. The outcome is intentionally NOT cached -- a strict
                        // validator always throws here and is never re-inspected via a result.
                        throw new AggregateValidationException(Merge(_errors, produced));
                    }
                }
            }

            // Cache only after a full, uncancelled run so a cancelled attempt can be retried.
            _asyncErrors = produced;
        }

        var merged = Merge(_errors, _asyncErrors);
        return merged.Count == 0 ? GuardResult.Success() : GuardResult.Failure(merged);
    }

    /// <summary>
    /// Merges the eagerly-collected synchronous errors with the async errors into a new list,
    /// surfacing sync failures first. Returns a fresh list each call so neither source is mutated.
    /// </summary>
    private static List<ValidationError> Merge(
        List<ValidationError> syncErrors, List<ValidationError> asyncErrors)
    {
        if (asyncErrors.Count == 0)
        {
            return syncErrors;
        }

        var merged = new List<ValidationError>(syncErrors.Count + asyncErrors.Count);
        merged.AddRange(syncErrors);
        merged.AddRange(asyncErrors);
        return merged;
    }

    /// <summary>
    /// Awaits all rules and throws <see cref="AggregateValidationException"/> if any error was
    /// produced; otherwise returns the validated instance. The async counterpart to
    /// <see cref="ThrowIfInvalid"/>.
    /// </summary>
    /// <param name="cancellationToken">A token observed before and during each async rule.</param>
    public async Task<T> ThrowIfInvalidAsync(CancellationToken cancellationToken = default)
    {
        var result = await ToResultAsync(cancellationToken).ConfigureAwait(false);
        result.ThrowIfInvalid();
        return _instance;
    }

    /// <summary>
    /// The async counterpart to <see cref="Build"/>: awaits all rules and returns the validated
    /// instance, throwing <see cref="AggregateValidationException"/> on failure.
    /// </summary>
    /// <param name="cancellationToken">A token observed before and during each async rule.</param>
    public Task<T> BuildAsync(CancellationToken cancellationToken = default) =>
        ThrowIfInvalidAsync(cancellationToken);

    private void ThrowIfAsyncRulesPending()
    {
        if (HasAsyncRules)
        {
            throw new InvalidOperationException(
                "This validator has asynchronous rules pending. Resolve it with ToResultAsync, " +
                "BuildAsync, or ThrowIfInvalidAsync so async rule results are not discarded.");
        }
    }

    private static Func<T, TProperty> GetOrCompileAccessor<TProperty>(Expression<Func<T, TProperty>> selector) =>
        AccessorCache<T, TProperty>.Get(selector);

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
    public static ObjectValidator<T> For<T>(T instance) where T : class
    {
        return new ObjectValidator<T>(instance, throwOnFirstError: false);
    }

    /// <summary>
    /// Creates an object validator that throws on first error.
    /// </summary>
    public static ObjectValidator<T> ForStrict<T>(T instance) where T : class
    {
        return new ObjectValidator<T>(instance, throwOnFirstError: true);
    }

    /// <summary>
    /// Creates a nested validator for deep hierarchical object graph validation.
    /// Supports unlimited-depth validation with full property path tracking.
    /// </summary>
    /// <typeparam name="T">The type of the root object to validate.</typeparam>
    /// <param name="instance">The object instance to validate.</param>
    /// <returns>A <see cref="NestedValidator{T}"/> for fluent configuration.</returns>
    /// <example>
    /// <code>
    /// var result = Validate.Nested(order)
    ///     .Property(o => o.OrderNumber, p => p.NotEmpty())
    ///     .Nested(o => o.Customer, customer => customer
    ///         .Property(c => c.Email, p => p.Email())
    ///         .Nested(c => c.Address, address => address
    ///             .Property(a => a.City, p => p.NotEmpty())))
    ///     .Collection(o => o.Items, (item, index) => item
    ///         .Property(i => i.ProductName, p => p.NotEmpty())
    ///         .Property(i => i.Quantity, p => p.GreaterThan(0)))
    ///     .ToResult();
    /// </code>
    /// </example>
    public static NestedValidator<T> Nested<T>(T instance) where T : class
    {
        return new NestedValidator<T>(instance);
    }

    /// <summary>
    /// Creates a fluent cross-property validator for the specified instance.
    /// Usage: Validate.CrossProperties(order).IsGreaterThan(o => o.EndDate, o => o.StartDate).ThrowIfInvalid();
    /// </summary>
    public static CrossPropertyValidator<T> CrossProperties<T>(T instance) where T : class => new(instance);

    /// <summary>
    /// Creates a <see cref="DeltaValidator{T}"/> that validates only properties changed
    /// between the two snapshots -- ideal for PATCH operations where the caller should
    /// not have to revalidate untouched fields.
    /// </summary>
    /// <typeparam name="T">The type of the patched object.</typeparam>
    /// <param name="original">
    /// The pre-change snapshot. Pass <c>null</c> to treat every property as changed
    /// (useful when validating newly created entities with the delta API).
    /// </param>
    /// <param name="updated">The post-change snapshot.</param>
    /// <example>
    /// <code>
    /// var result = Validate.Delta(originalUser, updatedUser)
    ///     .ForChanged(u => u.Email, g => g.NotNull().Email())
    ///     .ForChanged(u => u.Age, g => g.InRange(18, 120))
    ///     .WhenChanged(u => u.Password, d => d
    ///         .ForChanged(u => u.Password, g => g.MinLength(8)))
    ///     .ToResult();
    /// </code>
    /// </example>
    public static DeltaValidator<T> Delta<T>(T? original, T updated) where T : class =>
        new(new Delta<T>(original, updated));

    /// <summary>
    /// Creates a <see cref="DeltaValidator{T}"/> from a pre-constructed <c>Delta&lt;T&gt;</c>.
    /// </summary>
    public static DeltaValidator<T> Delta<T>(Delta<T> delta) where T : class => new(delta);

    /// <summary>
    /// Creates a polymorphic validator for the specified base type.
    /// Usage: Validate.Polymorphic&lt;PaymentBase&gt;().When&lt;CreditCard&gt;(cc => ...).Validate(payment);
    /// </summary>
    public static PolymorphicValidator<T> Polymorphic<T>() where T : class => new();

    /// <summary>
    /// Validates multiple objects and combines their results.
    /// </summary>
    public static GuardResult All(params GuardResult[] results)
    {
        return GuardResult.Combine(results);
    }
}
