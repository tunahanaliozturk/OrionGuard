using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Moongazing.OrionGuard.AspNetCore.Attributes;
using Moongazing.OrionGuard.AspNetCore.ProblemDetails;
using Moongazing.OrionGuard.DependencyInjection;

namespace Moongazing.OrionGuard.AspNetCore.Filters;

/// <summary>
/// MVC action filter that validates action arguments using the registered
/// <see cref="IValidator{T}"/> from the DI container. Only runs when the
/// <see cref="ValidateRequestAttribute"/> is present on the action or controller.
/// </summary>
public sealed class OrionGuardMvcFilter : IAsyncActionFilter
{
    /// <inheritdoc />
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var hasAttribute = context.ActionDescriptor.EndpointMetadata
            .OfType<ValidateRequestAttribute>()
            .Any();

        if (!hasAttribute)
        {
            await next().ConfigureAwait(false);
            return;
        }

        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is null)
            {
                continue;
            }

            var argumentType = argument.GetType();
            var validatorType = typeof(IValidator<>).MakeGenericType(argumentType);
            var validator = context.HttpContext.RequestServices.GetService(validatorType);

            if (validator is null)
            {
                continue;
            }

            var validateAsyncMethod = validatorType.GetMethod(nameof(IValidator<object>.ValidateAsync));

            if (validateAsyncMethod is null)
            {
                continue;
            }

            var task = (Task<Core.GuardResult>)validateAsyncMethod.Invoke(
                validator,
                [argument, context.HttpContext.RequestAborted])!;

            var result = await task.ConfigureAwait(false);

            if (result.IsInvalid)
            {
                var problemDetails = OrionGuardProblemDetailsFactory.Create(result);
                context.Result = new ObjectResult(problemDetails)
                {
                    StatusCode = problemDetails.Status
                };
                return;
            }
        }

        await next().ConfigureAwait(false);
    }
}
