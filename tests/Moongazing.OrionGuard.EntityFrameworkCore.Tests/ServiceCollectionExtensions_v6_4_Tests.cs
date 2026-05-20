using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Archival;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Locking;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox.TypeMap;

namespace Moongazing.OrionGuard.EntityFrameworkCore.Tests;

public class ServiceCollectionExtensions_v6_4_Tests
{
    private sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options) { }

    private static IServiceCollection Bootstrap(Action<OrionGuardEfCoreOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<TestDbContext>(o => o.UseSqlite("Filename=:memory:"));
        services.AddOrionGuardEfCore<TestDbContext>(o =>
        {
            o.UseOutbox();
            configure?.Invoke(o);
        });
        return services;
    }

    [Fact]
    public void AddOrionGuardEfCore_ShouldRegister_SkipLockedDistributedLock_ByDefault_InOutboxMode()
    {
        using var sp = Bootstrap().BuildServiceProvider();
        var @lock = sp.GetRequiredService<IDistributedLock>();
        Assert.IsType<SkipLockedDistributedLock>(@lock);
    }

    [Fact]
    public void AddOrionGuardEfCore_ShouldHonor_UseDistributedLock_Override()
    {
        using var sp = Bootstrap(o => o.UseDistributedLock<NullDistributedLock>()).BuildServiceProvider();
        var @lock = sp.GetRequiredService<IDistributedLock>();
        Assert.IsType<NullDistributedLock>(@lock);
    }

    [Fact]
    public void AddOrionGuardEfCore_ShouldRegister_EmptyTypeMapRegistry_ByDefault_InOutboxMode()
    {
        using var sp = Bootstrap().BuildServiceProvider();
        var registry = sp.GetRequiredService<OutboxTypeMapRegistry>();
        Assert.NotNull(registry);
        Assert.False(registry.TryResolve("anything", out _));
    }

    [Fact]
    public void AddOrionGuardEfCore_ShouldRegister_DefaultTypeMapOptions_InOutboxMode()
    {
        using var sp = Bootstrap().BuildServiceProvider();
        var options = sp.GetRequiredService<OutboxTypeMapOptions>();
        Assert.True(options.AllowAssemblyQualifiedNameFallback);
    }

    [Fact]
    public void AddOrionGuardEfCore_ShouldNotRegister_OutboxArchivalHostedService_Without_OptIn()
    {
        using var sp = Bootstrap().BuildServiceProvider();
        var hosted = sp.GetServices<IHostedService>();
        Assert.DoesNotContain(hosted, h => h is OutboxArchivalHostedService);
    }

    [Fact]
    public void AddOrionGuardEfCore_ShouldRegister_OutboxArchivalHostedService_When_OptedIn()
    {
        using var sp = Bootstrap(o => o.UseOutboxArchival(a => a.RetentionPeriod = TimeSpan.FromDays(7))).BuildServiceProvider();
        var hosted = sp.GetServices<IHostedService>();
        Assert.Contains(hosted, h => h is OutboxArchivalHostedService);

        var options = sp.GetRequiredService<OutboxArchivalOptions>();
        Assert.Equal(TimeSpan.FromDays(7), options.RetentionPeriod);
    }
}
