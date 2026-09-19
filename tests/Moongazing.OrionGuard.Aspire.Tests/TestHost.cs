using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moongazing.OrionGuard.EntityFrameworkCore;

namespace Moongazing.OrionGuard.Aspire.Tests;

/// <summary>Host and service helpers shared by the registration and health check tests.</summary>
internal static class TestHost
{
    public static HostApplicationBuilder NewBuilder() =>
        Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());

    /// <summary>Registers the EF Core outbox the way an app does, optionally with archival.</summary>
    public static void AddOutbox(IServiceCollection services, bool withArchival) =>
        services.AddOrionGuardEfCore<OrdersDbContext>(options =>
        {
            options.UseOutbox();
            if (withArchival)
            {
                options.UseOutboxArchival();
            }
        });

    public static IReadOnlyList<HealthCheckRegistration> HealthCheckRegistrations(IServiceProvider services) =>
        services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations.ToList();

    public static IReadOnlyList<string> HealthCheckNames(IServiceProvider services) =>
        HealthCheckRegistrations(services).Select(r => r.Name).ToList();

    // Never instantiated: AddOrionGuardEfCore only needs the type to register the outbox services.
    private sealed class OrdersDbContext : DbContext
    {
    }
}
