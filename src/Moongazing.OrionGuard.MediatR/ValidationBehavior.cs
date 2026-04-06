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
        if (!_validators.Any())
            return await next();

        var validationTasks = _validators.Select(v => v.ValidateAsync(request, cancellationToken));
        var results = await Task.WhenAll(validationTasks);
        var combined = GuardResult.Combine(results);

        combined.ThrowIfInvalid();

        return await next();
    }
}
