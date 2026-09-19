using System.Runtime.CompilerServices;
using MediatR;
using Moongazing.OrionGuard.Core;
using Moongazing.OrionGuard.DependencyInjection;

namespace Moongazing.OrionGuard.MediatR;

/// <summary>
/// MediatR stream pipeline behavior that validates <see cref="IStreamRequest{TResponse}"/> requests
/// (<c>IMediator.CreateStream</c>) with every registered IValidator before the handler yields its first item.
/// Throws <see cref="AggregateValidationException"/> on the first <c>MoveNextAsync</c> when validation fails.
/// </summary>
public sealed class StreamValidationBehavior<TRequest, TResponse> : IStreamPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public StreamValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
    {
        _validators = validators;
    }

    public async IAsyncEnumerable<TResponse> Handle(
        TRequest request,
        StreamHandlerDelegate<TResponse> next,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await ValidationBehavior<TRequest, TResponse>.ValidateAllAsync(_validators, request, cancellationToken)
            .ConfigureAwait(false);

        await foreach (var item in next().WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            yield return item;
        }
    }
}
