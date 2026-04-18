using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.DependencyInjection;

namespace Moongazing.OrionGuard.Tests;

public class StronglyTypedIdServiceExtensionsTests
{
    public sealed class FakeStrongIdEfCoreValueConverter { }

    [Fact]
    public void AddOrionGuardStronglyTypedIds_ShouldRegisterDiscoveredConverters_WhenScanningAssembly()
    {
        var services = new ServiceCollection();

        services.AddOrionGuardStronglyTypedIds(typeof(StronglyTypedIdServiceExtensionsTests).Assembly);

        var registered = services.Where(d => d.ServiceType == typeof(FakeStrongIdEfCoreValueConverter));
        Assert.Single(registered);
    }

    [Fact]
    public void AddOrionGuardStronglyTypedIds_ShouldBeIdempotent_WhenCalledTwice()
    {
        var services = new ServiceCollection();

        services.AddOrionGuardStronglyTypedIds(typeof(StronglyTypedIdServiceExtensionsTests).Assembly);
        services.AddOrionGuardStronglyTypedIds(typeof(StronglyTypedIdServiceExtensionsTests).Assembly);

        var registered = services.Where(d => d.ServiceType == typeof(FakeStrongIdEfCoreValueConverter));
        Assert.Single(registered);
    }
}
