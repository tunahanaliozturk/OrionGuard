using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Locking;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox.TypeMap;

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
    /// Multi-instance deployments are safe by default: the dispatcher uses
    /// <see cref="SkipLockedDistributedLock"/> against the <c>OrionGuard_OutboxLocks</c> table so
    /// only one replica dispatches at a time. Single-instance consumers who do not want this
    /// migration can opt into <see cref="NullDistributedLock"/> by registering it before calling
    /// this method.
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
            // Default outbox infrastructure: DB-backed distributed lock + empty logical-name
            // registry with AQN fallback enabled (v6.3 source-compatible). Consumers can override
            // by registering their own implementations BEFORE calling AddOrionGuardEfCore.
            services.TryAddSingleton<IDistributedLock, SkipLockedDistributedLock>();
            services.TryAddSingleton(new OutboxTypeMapRegistry());
            services.TryAddSingleton(new OutboxTypeMapOptions());

            services.AddHostedService(sp => new OutboxDispatcherHostedService(
                sp.GetRequiredService<OutboxOptions>(),
                sp.GetRequiredService<IServiceScopeFactory>(),
                sp.GetRequiredService<IDistributedLock>(),
                sp.GetRequiredService<OutboxTypeMapRegistry>(),
                sp.GetRequiredService<OutboxTypeMapOptions>(),
                sp.GetService<ILogger<OutboxDispatcherHostedService>>()));
        }
        return services;
    }
}
