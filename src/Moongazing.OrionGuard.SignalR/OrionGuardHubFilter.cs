using Microsoft.AspNetCore.SignalR;
using Moongazing.OrionGuard.Core;
using Moongazing.OrionGuard.DependencyInjection;

namespace Moongazing.OrionGuard.SignalR;

/// <summary>
/// SignalR Hub filter that validates method parameters using registered IValidator implementations.
/// </summary>
public sealed class OrionGuardHubFilter : IHubFilter
{
    private readonly IServiceProvider _serviceProvider;

    public OrionGuardHubFilter(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        var errors = new List<ValidationError>();

        foreach (var argument in invocationContext.HubMethodArguments)
        {
            if (argument is null) continue;

            var argumentType = argument.GetType();
            var validatorType = typeof(IValidator<>).MakeGenericType(argumentType);
            var validator = _serviceProvider.GetService(validatorType);

            if (validator is null) continue;

            var validateMethod = validatorType.GetMethod("Validate");
            if (validateMethod is null) continue;

            var result = validateMethod.Invoke(validator, new[] { argument }) as GuardResult;
            if (result?.IsInvalid == true)
            {
                errors.AddRange(result.Errors);
            }
        }

        if (errors.Count > 0)
        {
            var combined = GuardResult.Failure(errors);
            throw new HubException($"Validation failed: {combined.GetErrorSummary("; ")}");
        }

        return await next(invocationContext);
    }
}
