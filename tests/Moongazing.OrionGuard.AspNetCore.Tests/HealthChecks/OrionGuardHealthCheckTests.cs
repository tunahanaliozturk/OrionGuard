using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Moongazing.OrionGuard.AspNetCore.HealthChecks;
using Moongazing.OrionGuard.DependencyInjection;

namespace Moongazing.OrionGuard.AspNetCore.Tests.HealthChecks;

public class OrionGuardHealthCheckTests
{
    private static async Task<HealthCheckResult> CheckAsync(IServiceCollection services)
    {
        await using var provider = services.BuildServiceProvider();
        return await new OrionGuardHealthCheck(provider).CheckHealthAsync(new HealthCheckContext());
    }

    [Fact]
    public async Task CheckHealthAsync_ShouldReportTheLoadedCoreAssemblyVersion_WhenHealthy()
    {
        var expected = typeof(IValidatorFactory).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;

        var result = await CheckAsync(new ServiceCollection().AddOrionGuard());

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal(expected, result.Data["Version"]);
        Assert.NotEqual("6.0.0", result.Data["Version"]);
    }

    [Fact]
    public async Task CheckHealthAsync_ShouldReportOnlyTheValidatorFactoryAndVersion_WhenHealthy()
    {
        var result = await CheckAsync(new ServiceCollection().AddOrionGuard());

        // No exception-factory entry (guards never use one) and no hard-coded language count.
        Assert.Equal(new[] { "ValidatorFactory", "Version" }, result.Data.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("ValidatorFactory", result.Data["ValidatorFactory"]);
    }

    [Fact]
    public async Task CheckHealthAsync_ShouldBeHealthy_WhenNoExceptionFactoryIsRegistered()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IValidatorFactory, ValidatorFactory>();

        var result = await CheckAsync(services);

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task CheckHealthAsync_ShouldBeDegraded_WhenValidatorFactoryIsNotRegistered()
    {
        var result = await CheckAsync(new ServiceCollection());

        Assert.Equal(HealthStatus.Degraded, result.Status);
    }
}
