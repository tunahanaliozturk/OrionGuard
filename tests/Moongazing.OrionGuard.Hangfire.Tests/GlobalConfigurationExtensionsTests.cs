using System.Linq;
using Hangfire;
using Hangfire.Common;
using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.DependencyInjection;

namespace Moongazing.OrionGuard.Hangfire.Tests;

public sealed class GlobalConfigurationExtensionsTests
{
    private static ServiceProvider BuildProvider()
        => new ServiceCollection().AddOrionGuard().BuildServiceProvider();

    // The JobFilterCollection helper adds exactly one OrionGuardClientFilter.
    [Fact]
    public void AddOrionGuardClientFilter_AddsFilterToCollection()
    {
        using var provider = BuildProvider();
        var filters = new JobFilterCollection();

        filters.AddOrionGuardClientFilter(provider);

        Assert.Single(filters, f => f.Instance is OrionGuardClientFilter);
    }

    // The fluent helper returns a chainable configuration and registers the filter globally.
    // Hangfire's UseFilter wraps the configuration in IGlobalConfiguration<TFilter> (still an
    // IGlobalConfiguration, so the fluent chain continues), so reference equality is not expected.
    [Fact]
    public void UseOrionGuardValidation_ReturnsConfigurationAndRegistersFilter()
    {
        using var provider = BuildProvider();
        var configuration = GlobalConfiguration.Configuration;

        try
        {
            var returned = configuration.UseOrionGuardValidation(provider);

            Assert.NotNull(returned);
            Assert.IsAssignableFrom<IGlobalConfiguration>(returned);
            Assert.Contains(GlobalJobFilters.Filters, f => f.Instance is OrionGuardClientFilter);
        }
        finally
        {
            // Keep the process-wide global filter registry clean for any other test.
            GlobalJobFilters.Filters.Remove(typeof(OrionGuardClientFilter));
        }
    }

    [Fact]
    public void AddOrionGuardClientFilter_NullFilters_Throws()
    {
        using var provider = BuildProvider();
        JobFilterCollection filters = null!;

        Assert.Throws<ArgumentNullException>(() => filters.AddOrionGuardClientFilter(provider));
    }

    [Fact]
    public void AddOrionGuardClientFilter_NullServiceProvider_Throws()
    {
        var filters = new JobFilterCollection();

        Assert.Throws<ArgumentNullException>(() => filters.AddOrionGuardClientFilter(null!));
    }

    [Fact]
    public void UseOrionGuardValidation_NullConfiguration_Throws()
    {
        using var provider = BuildProvider();
        IGlobalConfiguration configuration = null!;

        Assert.Throws<ArgumentNullException>(() => configuration.UseOrionGuardValidation(provider));
    }

    [Fact]
    public void UseOrionGuardValidation_NullServiceProvider_Throws()
    {
        var configuration = GlobalConfiguration.Configuration;

        Assert.Throws<ArgumentNullException>(() => configuration.UseOrionGuardValidation(null!));
    }
}
