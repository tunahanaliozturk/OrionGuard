namespace Moongazing.OrionGuard.Outbox.PostgresNotify;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Push;

/// <summary>
/// DI helpers for the Postgres LISTEN/NOTIFY-backed outbox wake signal.
/// </summary>
public static class PostgresNotifyServiceCollectionExtensions
{
    /// <summary>
    /// Register <see cref="PostgresNotifyOutboxWakeSignal"/> as the <see cref="IOutboxWakeSignal"/>
    /// implementation AND as a hosted background service so the LISTEN loop starts with the host.
    /// The default <see cref="Moongazing.OrionGuard.EntityFrameworkCore.Outbox.OutboxDispatcherHostedService"/>
    /// will resolve this signal automatically because it ships before
    /// <c>NullOutboxWakeSignal</c> in the DI ordering.
    /// </summary>
    /// <param name="services">DI service collection.</param>
    /// <param name="configure">Required configuration callback for <see cref="PostgresNotifyOptions"/>.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="services"/> or <paramref name="configure"/> is null.</exception>
    public static IServiceCollection AddPostgresNotifyOutboxWakeSignal(
        this IServiceCollection services,
        Action<PostgresNotifyOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.Configure(configure);
        services.AddSingleton<PostgresNotifyOutboxWakeSignal>();
        services.Replace(ServiceDescriptor.Singleton<IOutboxWakeSignal>(
            sp => sp.GetRequiredService<PostgresNotifyOutboxWakeSignal>()));
        services.AddHostedService(sp => sp.GetRequiredService<PostgresNotifyOutboxWakeSignal>());

        return services;
    }
}
