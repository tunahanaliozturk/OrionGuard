using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.EntityFrameworkCore;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Locking;
using Moongazing.OrionLock.Providers;
using Moongazing.OrionLock.Redis;
using StackExchange.Redis;

namespace Moongazing.OrionGuard.Locks.Redis.Tests;

public class OrionGuardEfCoreOptionsExtensionsTests
{
    [Fact]
    public void UseOrionLockRedis_WithSharedMultiplexer_ReplacesIDistributedLock_AndRegistersOptions()
    {
        // We verify DI registration shape without ever resolving IConnectionMultiplexer.
        // The connection factory is wrapped in a lazy closure so the test stays Docker-free.
        var services = new ServiceCollection();
        services.AddSingleton<IConnectionMultiplexer>(_ =>
            throw new InvalidOperationException("Multiplexer factory should not run in this test."));

        var options = new OrionGuardEfCoreOptions().UseOutbox();
        options.UseOrionLockRedis(o => o.KeyPrefix = "x:");

        services.AddSingleton<IDistributedLock, NullDistributedLock>();
        foreach (var c in options.ServiceCustomizations)
        {
            c(services);
        }

        using var sp = services.BuildServiceProvider();

        var redisOpts = sp.GetRequiredService<RedisLockOptions>();
        Assert.Equal("x:", redisOpts.KeyPrefix);

        var lockDescriptor = services.Single(s => s.ServiceType == typeof(IDistributedLock));
        Assert.Equal(typeof(OrionLockBridgeDistributedLock), lockDescriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, lockDescriptor.Lifetime);

        var providerDescriptor = services.Single(s => s.ServiceType == typeof(IDistributedLockProvider));
        Assert.Equal(ServiceLifetime.Singleton, providerDescriptor.Lifetime);
        Assert.NotNull(providerDescriptor.ImplementationFactory);
    }

    [Fact]
    public void UseOrionLockRedis_ConnectionStringForm_RegistersMultiplexerFactory()
    {
        var services = new ServiceCollection();

        var options = new OrionGuardEfCoreOptions().UseOutbox();
        options.UseOrionLockRedis("localhost:6379", o => o.KeyPrefix = "cs:");

        services.AddSingleton<IDistributedLock, NullDistributedLock>();
        foreach (var c in options.ServiceCustomizations)
        {
            c(services);
        }

        var muxDescriptor = services.SingleOrDefault(s => s.ServiceType == typeof(IConnectionMultiplexer));
        Assert.NotNull(muxDescriptor);
        Assert.NotNull(muxDescriptor!.ImplementationFactory);
        Assert.Equal(ServiceLifetime.Singleton, muxDescriptor.Lifetime);

        var lockDescriptor = services.Single(s => s.ServiceType == typeof(IDistributedLock));
        Assert.Equal(typeof(OrionLockBridgeDistributedLock), lockDescriptor.ImplementationType);
    }

    [Fact]
    public void UseOrionLockRedis_NullArgs_Throw()
    {
        var options = new OrionGuardEfCoreOptions();
        Assert.Throws<ArgumentNullException>(() =>
            OrionGuardEfCoreOptionsExtensions.UseOrionLockRedis(null!, "localhost"));
        Assert.Throws<ArgumentException>(() => options.UseOrionLockRedis(""));
        Assert.Throws<ArgumentException>(() => options.UseOrionLockRedis("   "));
    }
}
