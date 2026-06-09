namespace Moongazing.OrionGuard.Outbox.SqlServerBroker;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Push;

/// <summary>
/// DI helpers for the SQL Server Service Broker-backed outbox wake signal.
/// </summary>
public static class SqlServerBrokerServiceCollectionExtensions
{
    /// <summary>
    /// Register <see cref="SqlServerBrokerOutboxWakeSignal"/> as the <see cref="IOutboxWakeSignal"/>
    /// implementation AND as a hosted background service so the WAITFOR loop starts with the
    /// host. Replaces the default <c>NullOutboxWakeSignal</c>.
    /// </summary>
    public static IServiceCollection AddSqlServerBrokerOutboxWakeSignal(
        this IServiceCollection services,
        Action<SqlServerBrokerOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.Configure(configure);
        services.AddSingleton<SqlServerBrokerOutboxWakeSignal>();
        services.Replace(ServiceDescriptor.Singleton<IOutboxWakeSignal>(
            sp => sp.GetRequiredService<SqlServerBrokerOutboxWakeSignal>()));
        services.AddHostedService(sp => sp.GetRequiredService<SqlServerBrokerOutboxWakeSignal>());

        return services;
    }
}
