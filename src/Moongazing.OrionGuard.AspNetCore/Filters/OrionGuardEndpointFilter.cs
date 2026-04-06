using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.AspNetCore.Options;
using Moongazing.OrionGuard.AspNetCore.ProblemDetails;
using Moongazing.OrionGuard.DependencyInjection;

namespace Moongazing.OrionGuard.AspNetCore.Filters;

/// <summary>
/// Minimal API endpoint filter that validates request parameters
/// using the registered <see cref="IValidator{T}"/> from the DI container.
/// Returns a <c>422 Unprocessable Entity</c> ProblemDetails response on validation failure.
/// </summary>
/// <typeparam name="TRequest">The type of the request parameter to validate.</typeparam>
public sealed class OrionGuardEndpointFilter<TRequest> : IEndpointFilter where TRequest : class
{
    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var validator = context.HttpContext.RequestServices.GetService(typeof(IValidator<TRequest>)) as IValidator<TRequest>;

        if (validator is null)
        {
            return await next(context).ConfigureAwait(false);
        }

        var request = FindRequestArgument(context);

        if (request is null)
        {
            return await next(context).ConfigureAwait(false);
        }

        var result = await validator.ValidateAsync(request, context.HttpContext.RequestAborted).ConfigureAwait(false);

        if (result.IsValid)
        {
            return await next(context).ConfigureAwait(false);
        }

        var options = context.HttpContext.RequestServices.GetService<OrionGuardAspNetCoreOptions>();
        var statusCode = options?.DefaultStatusCode ?? 422;

        if (options is null || options.UseProblemDetails)
        {
            var problemDetails = OrionGuardProblemDetailsFactory.Create(result);
            problemDetails.Status = statusCode;
            return Results.Problem(problemDetails);
        }

        return Results.Json(
            result.ToErrorDictionary(),
            statusCode: statusCode);
    }

    private static TRequest? FindRequestArgument(EndpointFilterInvocationContext context)
    {
        for (var i = 0; i < context.Arguments.Count; i++)
        {
            if (context.Arguments[i] is TRequest request)
            {
                return request;
            }
        }

        return null;
    }
}
