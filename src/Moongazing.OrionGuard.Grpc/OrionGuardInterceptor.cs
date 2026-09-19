using Grpc.Core;
using Grpc.Core.Interceptors;
using Moongazing.OrionGuard.DependencyInjection;

namespace Moongazing.OrionGuard.Grpc;

/// <summary>
/// gRPC server interceptor that validates incoming request messages using OrionGuard validators.
/// Throws RpcException with StatusCode.InvalidArgument when validation fails.
/// </summary>
/// <remarks>
/// Validators are resolved from the call's request scope (<c>HttpContext.RequestServices</c>), and every
/// validator registered for the message's runtime type runs through <c>ValidateAsync</c>, so scoped
/// validators and async rules work even though the interceptor itself is a singleton.
/// </remarks>
public sealed class OrionGuardInterceptor : Interceptor
{
    private readonly IServiceProvider _serviceProvider;

    /// <summary>Initializes a new instance of the <see cref="OrionGuardInterceptor"/> class.</summary>
    /// <param name="serviceProvider">
    /// Used only when a call has no <c>HttpContext</c> (the service is not hosted by ASP.NET Core).
    /// Otherwise validators come from the request scope.
    /// </param>
    public OrionGuardInterceptor(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        await ValidateRequestAsync(request, ResolveServices(context), context.CancellationToken).ConfigureAwait(false);
        return await continuation(request, context).ConfigureAwait(false);
    }

    public override async Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        ServerCallContext context,
        ClientStreamingServerMethod<TRequest, TResponse> continuation)
    {
        // Wrap the stream reader to validate each message
        var validatingStream = new ValidatingStreamReader<TRequest>(requestStream, ResolveServices(context));
        return await continuation(validatingStream, context).ConfigureAwait(false);
    }

    public override async Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        await ValidateRequestAsync(request, ResolveServices(context), context.CancellationToken).ConfigureAwait(false);
        await continuation(request, responseStream, context).ConfigureAwait(false);
    }

    public override async Task DuplexStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        DuplexStreamingServerMethod<TRequest, TResponse> continuation)
    {
        var validatingStream = new ValidatingStreamReader<TRequest>(requestStream, ResolveServices(context));
        await continuation(validatingStream, responseStream, context).ConfigureAwait(false);
    }

    private IServiceProvider ResolveServices(ServerCallContext context)
    {
        try
        {
            return context.GetHttpContext().RequestServices ?? _serviceProvider;
        }
        catch (InvalidOperationException)
        {
            // Why: GetHttpContext throws when the service is not hosted by ASP.NET Core, and there is no
            // non-throwing way to ask. Without a request scope, the injected provider is the only option.
            return _serviceProvider;
        }
    }

    private static async Task ValidateRequestAsync<TRequest>(
        TRequest request,
        IServiceProvider services,
        CancellationToken cancellationToken)
        where TRequest : class
    {
        var result = await ValidatorInvoker.ValidateAsync(services, request, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (result is { IsInvalid: true })
        {
            var metadata = new Metadata();
            var errorsJson = System.Text.Json.JsonSerializer.Serialize(
                result.Errors.Select(e => new { e.ParameterName, e.Message }));
            metadata.Add("validation-errors-json", errorsJson);

            throw new RpcException(
                new Status(StatusCode.InvalidArgument, result.GetErrorSummary("; ")),
                metadata);
        }
    }

    // Inner class for validating streaming requests
    private sealed class ValidatingStreamReader<T> : IAsyncStreamReader<T> where T : class
    {
        private readonly IAsyncStreamReader<T> _inner;
        private readonly IServiceProvider _services;

        public ValidatingStreamReader(IAsyncStreamReader<T> inner, IServiceProvider services)
        {
            _inner = inner;
            _services = services;
        }

        public T Current => _inner.Current;

        public async Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            if (!await _inner.MoveNext(cancellationToken).ConfigureAwait(false))
                return false;

            await ValidateRequestAsync(_inner.Current, _services, cancellationToken).ConfigureAwait(false);
            return true;
        }
    }
}
