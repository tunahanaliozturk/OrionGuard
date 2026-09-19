using Microsoft.AspNetCore.SignalR;
using Moongazing.OrionGuard.Core;
using Moongazing.OrionGuard.DependencyInjection;

namespace Moongazing.OrionGuard.SignalR;

/// <summary>
/// SignalR Hub filter that validates method parameters using registered IValidator implementations.
/// Validators are resolved from the hub invocation's service scope, and every validator registered for
/// an argument's runtime type runs through <c>ValidateAsync</c>, so async rules are enforced too.
/// </summary>
public sealed class OrionGuardHubFilter : IHubFilter
{
    /// <summary>Initializes a new instance of the <see cref="OrionGuardHubFilter"/> class.</summary>
    /// <param name="serviceProvider">
    /// Not used. Validators are resolved from <see cref="HubInvocationContext.ServiceProvider"/>, the
    /// per-invocation scope, so scoped validators get a correct lifetime. The parameter is kept so
    /// existing callers keep compiling.
    /// </param>
    public OrionGuardHubFilter(IServiceProvider serviceProvider)
    {
    }

    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        ArgumentNullException.ThrowIfNull(invocationContext);
        ArgumentNullException.ThrowIfNull(next);

        List<ValidationError>? errors = null;

        foreach (var argument in invocationContext.HubMethodArguments)
        {
            if (argument is null) continue;

            var result = await ValidatorInvoker.ValidateAsync(
                invocationContext.ServiceProvider,
                argument,
                cancellationToken: invocationContext.Context.ConnectionAborted).ConfigureAwait(false);

            if (result is { IsInvalid: true })
            {
                (errors ??= new List<ValidationError>()).AddRange(result.Errors);
            }
        }

        if (errors is not null)
        {
            // Errors keep argument order, then validator registration order, so the message is stable.
            var details = string.Join("; ", errors.Select(e =>
                string.IsNullOrEmpty(e.ParameterName) ? e.Message : $"{e.ParameterName}: {e.Message}"));
            throw new HubException($"Validation failed: {details}");
        }

        return await next(invocationContext).ConfigureAwait(false);
    }
}
