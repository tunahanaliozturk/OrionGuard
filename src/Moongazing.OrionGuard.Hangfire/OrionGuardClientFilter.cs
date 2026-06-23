using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using global::Hangfire.Client;
using global::Hangfire.Common;
using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.Core;
using Moongazing.OrionGuard.DependencyInjection;

namespace Moongazing.OrionGuard.Hangfire;

/// <summary>
/// Hangfire <see cref="IClientFilter"/> that validates a background job's arguments while the job is
/// being created (enqueued or scheduled). For each argument it resolves the matching
/// <see cref="IValidator{T}"/> from the application's <see cref="IServiceProvider"/> and runs it; if any
/// argument is invalid the filter throws <see cref="JobArgumentValidationException"/>, which Hangfire
/// surfaces to the caller that enqueued the job. The job is therefore rejected at enqueue time and never
/// reaches a worker. Arguments that are <see langword="null"/> or whose type has no registered validator
/// pass through untouched.
/// </summary>
/// <remarks>
/// <para>
/// This reuses the exact validator-resolution mechanism of the other OrionGuard integrations: a closed
/// <c>IValidator&lt;TArg&gt;</c> service is requested from DI for each argument's runtime type. Because the
/// job's argument types are only known at runtime, resolution and invocation go through reflection, the
/// same approach taken by the SignalR hub filter.
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
    private readonly IServiceProvider serviceProvider;

    /// <summary>Initializes a new instance of the <see cref="OrionGuardClientFilter"/> class.</summary>
    /// <param name="serviceProvider">
    /// The service provider used to resolve <see cref="IValidator{T}"/> instances for job arguments.
    /// Typically the application's root provider, captured at Hangfire configuration time.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="serviceProvider"/> is <see langword="null"/>.</exception>
    public OrionGuardClientFilter(IServiceProvider serviceProvider)
    {
        this.serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
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

        for (var i = 0; i < arguments.Count; i++)
        {
            var argument = arguments[i];
            if (argument is null)
            {
                continue;
            }

            var result = Validate(argument);
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
    /// Resolves the closed <see cref="IValidator{T}"/> for the argument's runtime type and runs it.
    /// Returns <see langword="null"/> when no validator is registered for the type.
    /// </summary>
    private GuardResult? Validate(object argument)
    {
        var argumentType = argument.GetType();
        var validatorType = typeof(IValidator<>).MakeGenericType(argumentType);

        var validator = serviceProvider.GetService(validatorType);
        if (validator is null)
        {
            return null;
        }

        var validateMethod = validatorType.GetMethod(
            nameof(IValidator<object>.Validate),
            new[] { argumentType });
        if (validateMethod is null)
        {
            return null;
        }

        try
        {
            return validateMethod.Invoke(validator, new[] { argument }) as GuardResult;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            // Surface the validator's real failure rather than the reflection wrapper so a throwing
            // validator is not masked as "no result". Hangfire treats this as a creation failure too.
            throw ex.InnerException;
        }
    }
}
