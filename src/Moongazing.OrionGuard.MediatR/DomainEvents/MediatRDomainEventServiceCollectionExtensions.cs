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
    public static IServiceCollection AddOrionGuardMediatRDomainEvents(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.RemoveAll<IDomainEventDispatcher>();
        services.AddScoped<IDomainEventDispatcher, MediatRDomainEventDispatcher>();
        return services;
    }
}
