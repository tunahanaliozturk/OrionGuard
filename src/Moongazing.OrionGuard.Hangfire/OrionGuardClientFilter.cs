using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.ExceptionServices;
using global::Hangfire.Client;
using global::Hangfire.Common;
using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.Core;
using Moongazing.OrionGuard.DependencyInjection;

namespace Moongazing.OrionGuard.Hangfire;

/// <summary>
/// Hangfire <see cref="IClientFilter"/> that validates a background job's arguments while the job is
/// being created (enqueued or scheduled). For each argument it resolves the matching
/// <see cref="IValidator{T}"/> from a freshly created DI scope and runs the full validation pipeline
/// (synchronous <i>and</i> asynchronous rules); if any argument is invalid the filter throws
/// <see cref="JobArgumentValidationException"/>, which Hangfire surfaces to the caller that enqueued the
/// job. The job is therefore rejected at enqueue time and never reaches a worker. Arguments that are
/// <see langword="null"/> or whose type has no registered validator pass through untouched.
/// </summary>
/// <remarks>
/// <para>
/// This reuses the exact validator-resolution mechanism of the other OrionGuard integrations: a closed
/// <c>IValidator&lt;TArg&gt;</c> service is requested from DI for each argument's runtime type. Because the
/// job's argument types are only known at runtime, resolution and invocation go through reflection, the
/// same approach taken by the SignalR hub filter.
/// </para>
/// <para>
/// <b>Lifetime.</b> The filter captures an <see cref="IServiceScopeFactory"/> rather than a resolution
/// provider, and opens a new <see cref="IServiceScope"/> for every <see cref="OnCreating"/> invocation.
/// Validators are resolved from that per-call scope, so scoped and transient validators (and any scoped
/// dependencies they pull in) get a correct, short-lived lifetime and are disposed when the scope is
/// disposed. Resolving them from the application's root provider instead would be a captive-dependency
/// bug: scoped registrations would either fail to resolve or be promoted to singleton lifetime, and
/// disposables would never be released.
/// </para>
/// <para>
/// <b>Async rules.</b> OrionGuard validators may declare asynchronous rules (e.g. <c>RuleForAsync</c>).
/// <see cref="IClientFilter.OnCreating"/> is synchronous, so the filter blocks on the async validation
/// pipeline (<c>IValidator&lt;T&gt;.ValidateAsync</c>) to
/// enforce those rules too. Blocking is acceptable here because enqueue is a foreground, non-hot-path
/// operation and a validator with only async rules must still be enforced at enqueue time.
/// </para>
/// <para>
/// Register the filter with <c>GlobalConfigurationExtensions.UseOrionGuardValidation</c> during Hangfire
/// configuration, or add it to <c>GlobalJobFilters.Filters</c> via
/// <c>GlobalConfigurationExtensions.AddOrionGuardClientFilter</c>.
/// </para>
/// </remarks>
[RequiresUnreferencedCode(
    "Resolves IValidator<T> for runtime job-argument types via reflection over the service provider. " +
    "Root the argument and validator types if you trim or publish with NativeAOT.")]
public sealed class OrionGuardClientFilter : IClientFilter
{
    private readonly IServiceScopeFactory scopeFactory;

    /// <summary>Initializes a new instance of the <see cref="OrionGuardClientFilter"/> class.</summary>
    /// <param name="scopeFactory">
    /// The scope factory used to open a fresh DI scope per job-creation so that
    /// <see cref="IValidator{T}"/> instances (and their scoped dependencies) are resolved with a correct
    /// lifetime instead of being captured against the root provider.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="scopeFactory"/> is <see langword="null"/>.</exception>
    public OrionGuardClientFilter(IServiceScopeFactory scopeFactory)
    {
        this.scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
    }

    /// <summary>Initializes a new instance of the <see cref="OrionGuardClientFilter"/> class.</summary>
    /// <param name="serviceProvider">
    /// The application's service provider. The filter resolves an <see cref="IServiceScopeFactory"/> from
    /// it once and opens a new scope per invocation; it does not resolve validators from this provider
    /// directly, so scoped validators are never captured against the root.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="serviceProvider"/> is <see langword="null"/>.</exception>
    public OrionGuardClientFilter(IServiceProvider serviceProvider)
        : this((serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider)))
            .GetRequiredService<IServiceScopeFactory>())
    {
    }

    /// <summary>
    /// Validates the job's arguments before the job is created. Throws
    /// <see cref="JobArgumentValidationException"/> when any argument is invalid, which cancels creation
    /// and propagates the failure to the enqueueing caller.
    /// </summary>
    /// <param name="context">The job-creation context supplied by Hangfire.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is <see langword="null"/>.</exception>
    /// <exception cref="JobArgumentValidationException">Thrown when one or more arguments fail validation.</exception>
    public void OnCreating(CreatingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var job = context.Job;
        if (job is null)
        {
            return;
        }

        var arguments = job.Args;
        if (arguments is null || arguments.Count == 0)
        {
            return;
        }

        List<ValidationError>? errors = null;

        // One scope per job-creation: scoped/transient validators and their scoped dependencies are
        // resolved with a correct lifetime and disposed when the scope is disposed.
        using var scope = scopeFactory.CreateScope();
        var provider = scope.ServiceProvider;

        for (var i = 0; i < arguments.Count; i++)
        {
            var argument = arguments[i];
            if (argument is null)
            {
                continue;
            }

            var result = Validate(provider, argument);
            if (result is { IsInvalid: true })
            {
                (errors ??= new List<ValidationError>()).AddRange(result.Errors);
            }
        }

        if (errors is { Count: > 0 })
        {
            throw new JobArgumentValidationException(errors, job.Type, job.Method?.Name);
        }
    }

    /// <summary>
    /// No-op. Validation happens entirely in <see cref="OnCreating"/> before the job is persisted.
    /// </summary>
    /// <param name="context">The job-created context supplied by Hangfire.</param>
    public void OnCreated(CreatedContext context)
    {
        // Intentionally empty: enqueue-time validation is complete once OnCreating returns.
    }

    /// <summary>
    /// Resolves the closed <see cref="IValidator{T}"/> for the argument's runtime type from
    /// <paramref name="provider"/> and runs the full validation pipeline (sync + async rules).
    /// Returns <see langword="null"/> when no validator is registered for the type.
    /// </summary>
    private static GuardResult? Validate(IServiceProvider provider, object argument)
    {
        var argumentType = argument.GetType();
        var validatorType = typeof(IValidator<>).MakeGenericType(argumentType);

        var validator = provider.GetService(validatorType);
        if (validator is null)
        {
            return null;
        }

        // Run the FULL validation, including async rules: a validator that declares only async rules
        // (RuleForAsync) is a no-op under the synchronous Validate(T) overload, so enqueue-time
        // enforcement must go through ValidateAsync(T, CancellationToken). OnCreating is synchronous, so
        // we block on the task here. Acceptable at enqueue time (foreground, not a worker hot path).
        var validateAsyncMethod = validatorType.GetMethod(
            nameof(IValidator<object>.ValidateAsync),
            new[] { argumentType, typeof(CancellationToken) });
        if (validateAsyncMethod is null)
        {
            return null;
        }

        object? invocationResult;
        try
        {
            invocationResult = validateAsyncMethod.Invoke(
                validator,
                new[] { argument, CancellationToken.None });
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            // The validator threw synchronously (before returning its Task). Surface the validator's
            // real failure with its original stack trace rather than the reflection wrapper, so a
            // throwing validator is not masked and the stack still points at the rule that failed.
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw; // unreachable; satisfies definite-assignment / flow analysis.
        }

        if (invocationResult is not Task<GuardResult> task)
        {
            return null;
        }

        // Block on the async pipeline. GetAwaiter().GetResult() rethrows the original exception
        // (with its stack preserved) instead of wrapping it in an AggregateException the way .Result/.Wait would.
        return task.GetAwaiter().GetResult();
    }
}
