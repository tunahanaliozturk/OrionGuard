using MediatR;
using Moongazing.OrionGuard.Core;
using Moongazing.OrionGuard.DependencyInjection;

namespace Moongazing.OrionGuard.MediatR;

/// <summary>
/// MediatR pipeline behavior that automatically validates requests using registered IValidator implementations.
/// Collects all validation errors before throwing AggregateValidationException.
/// </summary>
public sealed class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
    {
        _validators = validators;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        await ValidateAllAsync(_validators, request, cancellationToken).ConfigureAwait(false);
        return await next().ConfigureAwait(false);
    }

    /// <summary>
    /// Runs every validator and throws <see cref="AggregateValidationException"/> with the combined errors.
    /// Shared with <see cref="StreamValidationBehavior{TRequest, TResponse}"/>.
    /// </summary>
    internal static async Task ValidateAllAsync(
        IEnumerable<IValidator<TRequest>> validators,
        TRequest request,
        CancellationToken cancellationToken)
    {
        List<GuardResult>? results = null;

        // Why: validators run one after another, not concurrently. Validators resolved from the same
        // scope can share scoped dependencies such as a DbContext, which does not allow concurrent use.
        foreach (var validator in validators)
        {
            (results ??= new List<GuardResult>())
                .Add(await validator.ValidateAsync(request, cancellationToken).ConfigureAwait(false));
        }

        if (results is not null)
        {
            GuardResult.Combine(results.ToArray()).ThrowIfInvalid();
        }
    }
}
