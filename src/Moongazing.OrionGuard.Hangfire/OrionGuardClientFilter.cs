using System.Diagnostics.CodeAnalysis;
using global::Hangfire.Client;
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
/// Validation goes through <see cref="ValidatorInvoker"/>, the mechanism shared by the other OrionGuard
/// integrations: every <c>IValidator&lt;TArg&gt;</c> registered for each argument's runtime type is resolved
/// from DI and run, and their errors are combined. Because the job's argument types are only known at
/// runtime, the invoker builds its typed call through reflection once per type.
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

            // Run the FULL validation (every registered validator, including async rules): a validator that
            // declares only async rules (RuleForAsync) is a no-op under the synchronous Validate(T) overload.
            // OnCreating is synchronous, so we block on the task here. Acceptable at enqueue time (foreground,
            // not a worker hot path). GetAwaiter().GetResult() rethrows a throwing validator's original
            // exception with its stack preserved, instead of wrapping it in an AggregateException.
            var result = ValidatorInvoker.ValidateAsync(provider, argument).GetAwaiter().GetResult();
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
}
