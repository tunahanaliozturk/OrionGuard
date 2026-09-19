using Microsoft.Extensions.Hosting;
using static Moongazing.OrionGuard.Aspire.OrionGuardAspireExtensions;

namespace Moongazing.OrionGuard.Aspire.Tests;

public sealed class OrionGuardAspireRegistrationTests
{
    [Fact]
    public void AddOrionGuardDefaults_ShouldRegisterEverythingOnce_WhenCalledTwice()
    {
        var builder = TestHost.NewBuilder();
        TestHost.AddOutbox(builder.Services, withArchival: true);
        builder.AddOrionGuardDefaults(options => options.EnableOutboxArchivalHealthCheck = true);
        var serviceCountAfterFirstCall = builder.Services.Count;

        builder.AddOrionGuardDefaults();

        // No second telemetry subscription, health check hook, or marker.
        Assert.Equal(serviceCountAfterFirstCall, builder.Services.Count);
        using var host = builder.Build();
        var names = TestHost.HealthCheckNames(host.Services);
        Assert.Single(names, name => name == ValidationHealthCheckName);
        Assert.Single(names, name => name == OutboxArchivalHealthCheckName);
    }

    [Fact]
    public void AddOrionGuardDefaults_ShouldApplyEveryCallsConfiguration_WhenCalledTwice()
    {
        var builder = TestHost.NewBuilder();
        TestHost.AddOutbox(builder.Services, withArchival: true);
        // As in ServiceDefaults, then in the service itself.
        builder.AddOrionGuardDefaults(options => options.EnableOutboxArchivalHealthCheck = true);
        builder.AddOrionGuardDefaults(options => options.DisableValidationHealthCheck = true);
        using var host = builder.Build();

        var names = TestHost.HealthCheckNames(host.Services);
        Assert.DoesNotContain(ValidationHealthCheckName, names);
        Assert.Contains(OutboxArchivalHealthCheckName, names);
    }

    [Fact]
    public void AddOrionGuardDefaults_ShouldReturnTheSameBuilder_ForChaining()
    {
        var builder = TestHost.NewBuilder();

        HostApplicationBuilder returned = builder.AddOrionGuardDefaults();

        Assert.Same(builder, returned);
    }

    [Fact]
    public void AddOrionGuardDefaults_ShouldThrow_WhenBuilderIsNull()
    {
        HostApplicationBuilder builder = null!;

        Assert.Throws<ArgumentNullException>(() => builder.AddOrionGuardDefaults());
    }
}
