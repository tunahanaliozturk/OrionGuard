using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox;

namespace Moongazing.OrionGuard.EntityFrameworkCore;

/// <summary>DI extensions that wire OrionGuard's domain-event dispatching against an EF Core DbContext.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Configures OrionGuard's domain-event dispatching against an existing <typeparamref name="TDbContext"/>
    /// registration. Call after <c>services.AddDbContext&lt;TDbContext&gt;(...)</c>.
    /// </summary>
    /// <typeparam name="TDbContext">The consumer's <see cref="DbContext"/> type.</typeparam>
    /// <param name="services">The DI service collection.</param>
    /// <param name="configure">Optional configuration callback for <see cref="OrionGuardEfCoreOptions"/>.</param>
    /// <returns>The same <paramref name="services"/> instance for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// In Outbox mode, the consumer must apply <see cref="OutboxMessageEntityTypeConfiguration"/>
    /// inside their <c>OnModelCreating</c> override using the configured <see cref="OutboxOptions.TableName"/>;
    /// the <see cref="OutboxDispatcherHostedService"/> is registered automatically.
    /// <para>
    /// IMPORTANT: The v6.3.0 outbox dispatcher assumes a single instance per database.
    /// Concurrent instances will double-dispatch events because there is no row-level
    /// locking. If you scale horizontally (e.g. Kubernetes with replicas &gt; 1),
    /// either pin the worker to one replica via a leader-election mechanism, or run
    /// it in a dedicated singleton service. Distributed locking lands in v6.4.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddOrionGuardEfCore<TDbContext>(
        this IServiceCollection services,
        Action<OrionGuardEfCoreOptions>? configure = null)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new OrionGuardEfCoreOptions();
        configure?.Invoke(options);
        services.TryAddSingleton(options);
        services.TryAddSingleton(options.Outbox);
        services.TryAddScoped<DomainEventCollector>();
        services.TryAddScoped<DbContext>(sp => sp.GetRequiredService<TDbContext>());

        if (options.Strategy == DomainEventDispatchStrategy.Outbox)
        {
            services.AddHostedService(sp => new OutboxDispatcherHostedService(
                sp.GetRequiredService<OutboxOptions>(),
                sp.GetRequiredService<IServiceScopeFactory>(),
                sp.GetService<ILogger<OutboxDispatcherHostedService>>()));
        }
        return services;
    }
}
