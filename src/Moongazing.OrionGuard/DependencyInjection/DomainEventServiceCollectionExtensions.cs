using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moongazing.OrionGuard.Domain.Events;

namespace Moongazing.OrionGuard.DependencyInjection;

/// <summary>Extension methods on <see cref="IServiceCollection"/> that wire up OrionGuard's domain-event dispatcher.</summary>
public static class DomainEventServiceCollectionExtensions
{
    /// <summary>
    /// Registers the default <see cref="IDomainEventDispatcher"/> (<see cref="ServiceProviderDomainEventDispatcher"/>)
    /// and a <see cref="DomainEventDispatchOptions"/> singleton. Optionally configures the options.
    /// </summary>
    /// <param name="services">The service collection to augment.</param>
    /// <param name="configure">Callback to mutate the options instance before it is registered.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddOrionGuardDomainEvents(
        this IServiceCollection services,
        Action<DomainEventDispatchOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new DomainEventDispatchOptions();
        configure?.Invoke(options);
        services.TryAddSingleton(options);
        services.TryAddScoped<IDomainEventDispatcher, ServiceProviderDomainEventDispatcher>();
        return services;
    }

    /// <summary>
    /// Scans the supplied assemblies for concrete classes implementing <see cref="IDomainEventHandler{TEvent}"/>
    /// and registers each closed interface to its implementing type as <see cref="ServiceLifetime.Scoped"/>.
    /// </summary>
    /// <param name="services">The service collection to augment.</param>
    /// <param name="assemblies">Assemblies to scan. Must not be null.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddOrionGuardDomainEventHandlers(
        this IServiceCollection services,
        params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(assemblies);

        foreach (var assembly in assemblies)
        {
            foreach (var type in assembly.GetTypes())
            {
                if (type.IsAbstract || type.IsInterface) continue;
                if (type.IsGenericTypeDefinition) continue;
                foreach (var iface in type.GetInterfaces())
                {
                    if (!iface.IsGenericType) continue;
                    if (iface.GetGenericTypeDefinition() != typeof(IDomainEventHandler<>)) continue;
                    services.AddScoped(iface, type);
                }
            }
        }
        return services;
    }
}
