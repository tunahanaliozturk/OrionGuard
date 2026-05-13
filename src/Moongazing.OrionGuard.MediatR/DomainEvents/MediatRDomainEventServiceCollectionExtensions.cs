using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moongazing.OrionGuard.Domain.Events;

namespace Moongazing.OrionGuard.MediatR.DomainEvents;

/// <summary>DI extensions that swap the OrionGuard dispatcher for the MediatR-based one.</summary>
public static class MediatRDomainEventServiceCollectionExtensions
{
    /// <summary>
    /// Replaces any existing <see cref="IDomainEventDispatcher"/> registration with
    /// <see cref="MediatRDomainEventDispatcher"/>. Call after <c>services.AddMediatR(...)</c>.
    /// </summary>
    /// <param name="services">The DI service collection to mutate.</param>
    /// <returns>The same <paramref name="services"/> instance for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the OpenTelemetry decoration has already been applied (detected by looking for the
    /// <c>WithOpenTelemetryDomainEventsMarker</c> sentinel). The MediatR bridge wipes any prior
    /// <see cref="IDomainEventDispatcher"/> registration, including the OpenTelemetry decorator, so it must
    /// be registered first.
    /// </exception>
    /// <remarks>
    /// Call this BEFORE the OpenTelemetry decoration extension <c>WithOpenTelemetryDomainEvents()</c>.
    /// Calling it after will throw, since this method wipes any prior <see cref="IDomainEventDispatcher"/>
    /// registration including the OpenTelemetry decorator. Correct order:
    /// <c>AddOrionGuardDomainEvents()</c> -&gt; <c>AddOrionGuardMediatRDomainEvents()</c> -&gt;
    /// <c>WithOpenTelemetryDomainEvents()</c>.
    /// </remarks>
    public static IServiceCollection AddOrionGuardMediatRDomainEvents(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (services.Any(d => d.ServiceType.FullName == "Moongazing.OrionGuard.OpenTelemetry.DomainEvents.WithOpenTelemetryDomainEventsMarker"))
        {
            throw new InvalidOperationException(
                "AddOrionGuardMediatRDomainEvents() must be called BEFORE WithOpenTelemetryDomainEvents(). " +
                "Re-order your DI registrations: AddOrionGuardDomainEvents() -> AddOrionGuardMediatRDomainEvents() -> WithOpenTelemetryDomainEvents().");
        }

        services.RemoveAll<IDomainEventDispatcher>();
        services.AddScoped<IDomainEventDispatcher, MediatRDomainEventDispatcher>();
        return services;
    }
}
