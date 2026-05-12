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
    /// Thrown when no <see cref="IDomainEventDispatcher"/> is registered, or when the existing descriptor
    /// has no implementation type, factory, or instance.
    /// </exception>
    /// <remarks>
    /// Supports inner dispatchers registered by type, by factory, or by instance. Calling this method more
    /// than once is a no-op: re-entry is detected via the <see cref="WithOpenTelemetryDomainEventsMarker"/>
    /// sentinel that is registered alongside the decorator on the first call.
    /// </remarks>
    public static IServiceCollection WithOpenTelemetryDomainEvents(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Already decorated? No-op (detected via the marker sentinel).
        if (services.Any(d => d.ServiceType == typeof(WithOpenTelemetryDomainEventsMarker)))
        {
            return services;
        }

        var existing = services.LastOrDefault(d => d.ServiceType == typeof(IDomainEventDispatcher))
            ?? throw new InvalidOperationException(
                "No IDomainEventDispatcher is registered. " +
                "Call services.AddOrionGuardDomainEvents() (or services.AddOrionGuardMediatRDomainEvents()) first.");

        services.Remove(existing);
        services.AddSingleton<WithOpenTelemetryDomainEventsMarker>();

        services.Add(new ServiceDescriptor(
            typeof(IDomainEventDispatcher),
            sp =>
            {
                IDomainEventDispatcher inner;
                if (existing.ImplementationType is not null)
                {
                    inner = (IDomainEventDispatcher)ActivatorUtilities.CreateInstance(sp, existing.ImplementationType);
                }
                else if (existing.ImplementationFactory is not null)
                {
                    inner = (IDomainEventDispatcher)existing.ImplementationFactory(sp);
                }
                else if (existing.ImplementationInstance is not null)
                {
                    inner = (IDomainEventDispatcher)existing.ImplementationInstance;
                }
                else
                {
                    throw new InvalidOperationException(
                        "ServiceDescriptor for IDomainEventDispatcher has neither ImplementationType, ImplementationFactory, nor ImplementationInstance.");
                }
                return new InstrumentedDomainEventDispatcher(inner);
            },
            existing.Lifetime));

        return services;
    }
}

/// <summary>
/// Marker type registered when <see cref="DomainEventOpenTelemetryExtensions.WithOpenTelemetryDomainEvents"/>
/// has been applied to an <see cref="IServiceCollection"/>. Used to detect double-application so the
/// extension method is idempotent.
/// </summary>
public sealed class WithOpenTelemetryDomainEventsMarker
{
}
