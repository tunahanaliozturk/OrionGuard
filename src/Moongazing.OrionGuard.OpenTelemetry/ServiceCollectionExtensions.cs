using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moongazing.OrionGuard.DependencyInjection;

namespace Moongazing.OrionGuard.OpenTelemetry;

/// <summary>
/// Extension methods for registering OrionGuard OpenTelemetry instrumentation with the DI container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Decorates all registered <see cref="IValidator{T}"/> implementations with OpenTelemetry instrumentation.
    /// This adds distributed tracing spans and validation metrics (count, failure rate, duration) to every validation call.
    /// Must be called after all validators have been registered.
    /// </summary>
    public static IServiceCollection AddOrionGuardOpenTelemetry(this IServiceCollection services)
    {
        var validatorDescriptors = services
            .Where(d => d.ServiceType.IsGenericType && d.ServiceType.GetGenericTypeDefinition() == typeof(IValidator<>))
            .ToList();

        foreach (var descriptor in validatorDescriptors)
        {
            var serviceType = descriptor.ServiceType;
            var modelType = serviceType.GetGenericArguments()[0];
            var instrumentedType = typeof(InstrumentedValidator<>).MakeGenericType(modelType);

            services.Remove(descriptor);

            // Re-register the original implementation as its concrete type so it can be resolved by the wrapper
            if (descriptor.ImplementationType is not null)
            {
                services.TryAdd(new ServiceDescriptor(descriptor.ImplementationType, descriptor.ImplementationType, descriptor.Lifetime));
            }

            // Register the instrumented decorator as the IValidator<T> implementation
            services.Add(new ServiceDescriptor(
                serviceType,
                sp =>
                {
                    var innerValidator = descriptor.ImplementationType is not null
                        ? sp.GetRequiredService(descriptor.ImplementationType)
                        : descriptor.ImplementationFactory is not null
                            ? descriptor.ImplementationFactory(sp)
                            : descriptor.ImplementationInstance!;

                    return Activator.CreateInstance(instrumentedType, innerValidator)!;
                },
                descriptor.Lifetime));
        }

        return services;
    }
}
