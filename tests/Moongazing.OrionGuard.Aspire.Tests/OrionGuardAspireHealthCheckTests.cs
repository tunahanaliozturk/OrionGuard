using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Moongazing.OrionGuard.AspNetCore.Extensions;
using Moongazing.OrionGuard.AspNetCore.HealthChecks;
using Moongazing.OrionGuard.DependencyInjection;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Archival;
using static Moongazing.OrionGuard.Aspire.OrionGuardAspireExtensions;

namespace Moongazing.OrionGuard.Aspire.Tests;

public sealed class OrionGuardAspireHealthCheckTests
{
    [Fact]
    public void ValidationHealthCheck_ShouldBeRegistered_WhenOrionGuardAspNetCoreIsReferenced()
    {
        var builder = TestHost.NewBuilder();
        builder.AddOrionGuardDefaults();
        using var host = builder.Build();

        var registration = Assert.Single(
            TestHost.HealthCheckRegistrations(host.Services), r => r.Name == ValidationHealthCheckName);
        // The package finds the check by type name; this pins that name to the real type.
        Assert.IsType<OrionGuardHealthCheck>(registration.Factory(host.Services));
        Assert.Equal(new[] { "orionguard", "validation" }, registration.Tags.Order());
    }

    [Fact]
    public void ValidationHealthCheck_ShouldNotBeRegistered_WhenDisabled()
    {
        var builder = TestHost.NewBuilder();
        builder.AddOrionGuardDefaults(options => options.DisableValidationHealthCheck = true);
        using var host = builder.Build();

        Assert.DoesNotContain(ValidationHealthCheckName, TestHost.HealthCheckNames(host.Services));
    }

    [Fact]
    public void OutboxArchivalHealthCheck_ShouldNotBeRegistered_ByDefault()
    {
        var builder = TestHost.NewBuilder();
        TestHost.AddOutbox(builder.Services, withArchival: true);
        builder.AddOrionGuardDefaults();
        using var host = builder.Build();

        var names = TestHost.HealthCheckNames(host.Services);
        Assert.DoesNotContain(OutboxArchivalHealthCheckName, names);
        Assert.Contains(ValidationHealthCheckName, names);
    }

    [Fact]
    public void OutboxArchivalHealthCheck_ShouldBeRegistered_WhenEnabledAndArchivalIsAddedAfterTheCall()
    {
        var builder = TestHost.NewBuilder();
        // ServiceDefaults usually runs before the service registers its outbox.
        builder.AddOrionGuardDefaults(options => options.EnableOutboxArchivalHealthCheck = true);
        TestHost.AddOutbox(builder.Services, withArchival: true);
        using var host = builder.Build();

        var registration = Assert.Single(
            TestHost.HealthCheckRegistrations(host.Services), r => r.Name == OutboxArchivalHealthCheckName);
        // The package finds the check by type name; this pins that name to the real type.
        Assert.IsType<OutboxArchivalHealthCheck>(registration.Factory(host.Services));
        Assert.Equal(new[] { "orionguard", "outbox" }, registration.Tags.Order());
    }

    [Fact]
    public void OutboxArchivalHealthCheck_ShouldNotBeRegistered_WhenEnabledButTheOutboxRunsWithoutArchival()
    {
        var builder = TestHost.NewBuilder();
        TestHost.AddOutbox(builder.Services, withArchival: false);
        builder.AddOrionGuardDefaults(options => options.EnableOutboxArchivalHealthCheck = true);
        using var host = builder.Build();

        Assert.DoesNotContain(OutboxArchivalHealthCheckName, TestHost.HealthCheckNames(host.Services));
    }

    [Fact]
    public async Task HealthReport_ShouldIncludeBothChecks_WhenValidationAndOutboxArchivalAreInUse()
    {
        var builder = TestHost.NewBuilder();
        builder.Services.AddOrionGuard();
        TestHost.AddOutbox(builder.Services, withArchival: true);
        builder.AddOrionGuardDefaults(options => options.EnableOutboxArchivalHealthCheck = true);
        using var host = builder.Build();

        var report = await host.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync();

        Assert.Equal(HealthStatus.Healthy, report.Entries[ValidationHealthCheckName].Status);
        // The archival worker has not completed a batch yet, which the check reports as Degraded.
        Assert.Equal(HealthStatus.Degraded, report.Entries[OutboxArchivalHealthCheckName].Status);
    }

    [Fact]
    public async Task ValidationHealthCheck_ShouldBeRegisteredOnce_WhenTheAppAlsoCallsAddOrionGuardCheck()
    {
        var builder = TestHost.NewBuilder();
        builder.AddOrionGuardDefaults();
        builder.Services.AddHealthChecks().AddOrionGuardCheck();
        using var host = builder.Build();

        Assert.Single(TestHost.HealthCheckNames(host.Services), name => name == ValidationHealthCheckName);
        // HealthCheckService rejects duplicate names, so a completed report also proves there is no duplicate.
        var report = await host.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync();
        Assert.True(report.Entries.ContainsKey(ValidationHealthCheckName));
    }
}
