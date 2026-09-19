using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.AspNetCore.Attributes;
using Moongazing.OrionGuard.AspNetCore.Options;
using Moongazing.OrionGuard.AspNetCore.ProblemDetails;
using Moongazing.OrionGuard.DependencyInjection;

namespace Moongazing.OrionGuard.AspNetCore.Filters;

/// <summary>
/// MVC action filter that validates action arguments using every <see cref="IValidator{T}"/>
/// registered for the argument's runtime type in the request services. Only runs when the
/// <see cref="ValidateRequestAttribute"/> is present on the action or controller; the attribute
/// adds this filter to the pipeline.
/// </summary>
/// <remarks>
/// Failures are written the same way as <see cref="OrionGuardEndpointFilter{TRequest}"/>: status
/// <see cref="OrionGuardAspNetCoreOptions.DefaultStatusCode"/>, as <c>ValidationProblemDetails</c> unless
/// <see cref="OrionGuardAspNetCoreOptions.UseProblemDetails"/> is <see langword="false"/>.
/// </remarks>
public sealed class OrionGuardMvcFilter : IAsyncActionFilter
{
    /// <inheritdoc />
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var hasAttribute = context.ActionDescriptor.EndpointMetadata
            .OfType<ValidateRequestAttribute>()
            .Any();

        // Why: [ValidateRequest] on both the controller and the action (or a global registration plus the
        // attribute) creates one filter per scope. Only the most specific one validates, so validators
        // run once per request.
        if (!hasAttribute || (context.FindEffectivePolicy<OrionGuardMvcFilter>() is { } effective && effective != this))
        {
            await next().ConfigureAwait(false);
            return;
        }

        var services = context.HttpContext.RequestServices;

        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is null)
            {
                continue;
            }

            var result = await ValidatorInvoker.ValidateAsync(
                services,
                argument,
                cancellationToken: context.HttpContext.RequestAborted).ConfigureAwait(false);

            if (result is { IsInvalid: true })
            {
                var options = services.GetService<OrionGuardAspNetCoreOptions>();
                // A validator that returns FailureWithStatus (e.g. 409) knows better than the global default.
                var statusCode = result.SuggestedHttpStatusCode ?? options?.DefaultStatusCode ?? 422;

                if (options is null || options.UseProblemDetails)
                {
                    var problemDetails = OrionGuardProblemDetailsFactory.Create(result);
                    problemDetails.Status = statusCode;
                    context.Result = new ObjectResult(problemDetails) { StatusCode = statusCode };
                }
                else
                {
                    context.Result = new ObjectResult(result.ToErrorDictionary()) { StatusCode = statusCode };
                }

                return;
            }
        }

        await next().ConfigureAwait(false);
    }
}
