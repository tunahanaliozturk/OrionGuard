using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moongazing.OrionGuard.EntityFrameworkCore;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Locking;
using Moongazing.OrionLock.Providers;
using Moongazing.OrionLock.Redis;
using StackExchange.Redis;

namespace Moongazing.OrionGuard.Locks.Redis;

/// <summary>
/// Fluent <see cref="OrionGuardEfCoreOptions"/> extensions that swap the registered
/// <see cref="IDistributedLock"/> for the Redis-backed bridge from this package.
/// </summary>
/// <remarks>
/// Place the call <em>after</em> <c>UseOutbox(...)</c> but inside the same
/// <c>AddOrionGuardEfCore</c> configuration callback so the default
/// <c>SkipLockedDistributedLock</c> is replaced before the dispatcher hosted service is wired.
/// </remarks>
public static class OrionGuardEfCoreOptionsExtensions
{
    /// <summary>
    /// Registers the OrionLock Redis backend and swaps OrionGuard's <see cref="IDistributedLock"/>
    /// for <see cref="OrionLockBridgeDistributedLock"/>. Uses <paramref name="connectionString"/>
    /// to build a singleton <see cref="IConnectionMultiplexer"/>.
    /// </summary>
    /// <param name="options">The OrionGuard EF Core options being configured.</param>
    /// <param name="connectionString">A StackExchange.Redis-compatible connection string.</param>
    /// <param name="configure">Optional callback to tune <see cref="RedisLockOptions"/> (key prefix, db index).</param>
    /// <returns>The same <paramref name="options"/> for chaining.</returns>
    public static OrionGuardEfCoreOptions UseOrionLockRedis(
        this OrionGuardEfCoreOptions options,
        string connectionString,
        Action<RedisLockOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var redisOptions = new RedisLockOptions();
        configure?.Invoke(redisOptions);

        options.ServiceCustomizations.Add(services =>
        {
            services.TryAddSingleton<IConnectionMultiplexer>(_ =>
                ConnectionMultiplexer.Connect(connectionString));
            RegisterBridge(services, redisOptions);
        });

        return options;
    }

    /// <summary>
    /// Registers the OrionLock Redis backend over an already-registered
    /// <see cref="IConnectionMultiplexer"/> singleton and swaps OrionGuard's
    /// <see cref="IDistributedLock"/> for <see cref="OrionLockBridgeDistributedLock"/>.
    /// </summary>
    /// <param name="options">The OrionGuard EF Core options being configured.</param>
    /// <param name="configure">Optional callback to tune <see cref="RedisLockOptions"/>.</param>
    /// <returns>The same <paramref name="options"/> for chaining.</returns>
    /// <remarks>
    /// The consumer must register <see cref="IConnectionMultiplexer"/> themselves before
    /// the <see cref="ServiceProvider"/> is built. This overload is preferred when other
    /// parts of the application already share a Redis multiplexer.
    /// </remarks>
    public static OrionGuardEfCoreOptions UseOrionLockRedis(
        this OrionGuardEfCoreOptions options,
        Action<RedisLockOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        var redisOptions = new RedisLockOptions();
        configure?.Invoke(redisOptions);

        options.ServiceCustomizations.Add(services => RegisterBridge(services, redisOptions));

        return options;
    }

    private static void RegisterBridge(IServiceCollection services, RedisLockOptions redisOptions)
    {
        services.TryAddSingleton(redisOptions);
        services.TryAddSingleton<IDistributedLockProvider>(sp =>
            new RedisLockProvider(
                sp.GetRequiredService<IConnectionMultiplexer>(),
                sp.GetRequiredService<RedisLockOptions>()));
        services.Replace(ServiceDescriptor.Singleton<IDistributedLock, OrionLockBridgeDistributedLock>());
    }
}
