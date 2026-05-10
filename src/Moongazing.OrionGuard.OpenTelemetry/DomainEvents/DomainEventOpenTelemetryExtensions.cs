using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.Domain.Events;

namespace Moongazing.OrionGuard.OpenTelemetry.DomainEvents;

/// <summary>DI extension that wraps the registered <see cref="IDomainEventDispatcher"/> with OpenTelemetry instrumentation.</summary>
public static class DomainEventOpenTelemetryExtensions
{
    /// <summary>
    /// Wraps the registered <see cref="IDomainEventDispatcher"/> with
    /// <see cref="InstrumentedDomainEventDispatcher"/>. Call after <c>AddOrionGuardDomainEvents()</c>
    /// (or after the MediatR bridge registration if you use it).
    /// </summary>
    /// <param name="services">The service collection to mutate.</param>
    /// <returns>The same <paramref name="services"/> instance for fluent chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no <see cref="IDomainEventDispatcher"/> is registered, when more than one is registered,
    /// or when the inner dispatcher was not registered by concrete type.
    /// </exception>
    public static IServiceCollection WithOpenTelemetryDomainEvents(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var existing = services.Single(d => d.ServiceType == typeof(IDomainEventDispatcher));
        services.Remove(existing);

        services.Add(new ServiceDescriptor(
            typeof(IDomainEventDispatcher),
            sp =>
            {
                var implType = existing.ImplementationType
                    ?? throw new InvalidOperationException("Inner dispatcher must be registered by type for OpenTelemetry decoration.");
                var inner = (IDomainEventDispatcher)ActivatorUtilities.CreateInstance(sp, implType);
                return new InstrumentedDomainEventDispatcher(inner);
            },
            existing.Lifetime));
        return services;
    }
}
