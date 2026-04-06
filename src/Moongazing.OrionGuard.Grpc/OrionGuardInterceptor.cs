using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.Core;
using Moongazing.OrionGuard.DependencyInjection;

namespace Moongazing.OrionGuard.Grpc;

/// <summary>
/// gRPC server interceptor that validates incoming request messages using OrionGuard validators.
/// Throws RpcException with StatusCode.InvalidArgument when validation fails.
/// </summary>
public sealed class OrionGuardInterceptor : Interceptor
{
    private readonly IServiceProvider _serviceProvider;

    public OrionGuardInterceptor(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        ValidateRequest(request);
        return await continuation(request, context);
    }

    public override async Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        ServerCallContext context,
        ClientStreamingServerMethod<TRequest, TResponse> continuation)
    {
        // Wrap the stream reader to validate each message
        var validatingStream = new ValidatingStreamReader<TRequest>(requestStream, this);
        return await continuation(validatingStream, context);
    }

    public override async Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        ValidateRequest(request);
        await continuation(request, responseStream, context);
    }

    public override async Task DuplexStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        DuplexStreamingServerMethod<TRequest, TResponse> continuation)
    {
        var validatingStream = new ValidatingStreamReader<TRequest>(requestStream, this);
        await continuation(validatingStream, responseStream, context);
    }

    internal void ValidateRequest<TRequest>(TRequest request) where TRequest : class
    {
        var validator = _serviceProvider.GetService<IValidator<TRequest>>();
        if (validator is null) return;

        var result = validator.Validate(request);
        if (result.IsInvalid)
        {
            var metadata = new Metadata();
            foreach (var error in result.Errors)
            {
                metadata.Add($"validation-error-{error.ParameterName}", error.Message);
            }

            throw new RpcException(
                new Status(StatusCode.InvalidArgument, result.GetErrorSummary("; ")),
                metadata);
        }
    }

    // Inner class for validating streaming requests
    private sealed class ValidatingStreamReader<T> : IAsyncStreamReader<T> where T : class
    {
        private readonly IAsyncStreamReader<T> _inner;
        private readonly OrionGuardInterceptor _interceptor;

        public ValidatingStreamReader(IAsyncStreamReader<T> inner, OrionGuardInterceptor interceptor)
        {
            _inner = inner;
            _interceptor = interceptor;
        }

        public T Current => _inner.Current;

        public async Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            if (!await _inner.MoveNext(cancellationToken))
                return false;

            _interceptor.ValidateRequest(_inner.Current);
            return true;
        }
    }
}
