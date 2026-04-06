using Microsoft.Extensions.DependencyInjection;

namespace Moongazing.OrionGuard.Grpc;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds OrionGuard gRPC validation interceptor.
    /// Usage: services.AddOrionGuardGrpc();
    /// Then in gRPC service registration: services.AddGrpc(o => o.Interceptors.Add&lt;OrionGuardInterceptor&gt;());
    /// </summary>
    public static IServiceCollection AddOrionGuardGrpc(this IServiceCollection services)
    {
        services.AddSingleton<OrionGuardInterceptor>();
        return services;
    }
}
