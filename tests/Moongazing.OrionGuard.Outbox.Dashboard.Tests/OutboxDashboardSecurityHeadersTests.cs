namespace Moongazing.OrionGuard.Outbox.Dashboard.Tests;

using System.Net.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moongazing.OrionGuard.Outbox.Dashboard;

public sealed class OutboxDashboardSecurityHeadersTests
{
    private sealed class SecDbContext : DbContext
    {
        public SecDbContext(DbContextOptions<SecDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Moongazing.OrionGuard.EntityFrameworkCore.Outbox.OutboxMessage>(b =>
            {
                b.ToTable("OutboxMessages");
                b.HasKey(x => x.Id);
            });
        }
    }

    private static async Task<HttpResponseMessage> RequestAsync(
        Action<OutboxDashboardOptions>? configure = null)
    {
        var dbName = "sec-tests-" + Guid.NewGuid().ToString("N");
        var inMemoryRoot = new Microsoft.EntityFrameworkCore.Storage.InMemoryDatabaseRoot();
        var host = await new HostBuilder()
            .ConfigureWebHost(builder =>
            {
                builder.UseTestServer();
                builder.ConfigureServices(s =>
                {
                    s.AddRouting();
                    s.AddDbContext<SecDbContext>(opts => opts
                        .UseInMemoryDatabase(dbName, inMemoryRoot)
                        .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)));
                });
                builder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(e => e.MapOutboxDashboard<SecDbContext>(o =>
                    {
                        o.AllowAnonymous = true;
                        configure?.Invoke(o);
                    }));
                });
            })
            .StartAsync();
        var client = host.GetTestClient();
        return await client.GetAsync("/_orion/outbox/failed");
    }

    [Fact]
    public async Task Failed_endpoint_response_carries_default_security_headers()
    {
        var response = await RequestAsync();

        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
    }

    [Fact]
    public async Task SecurityHeaders_can_be_replaced_with_custom_set()
    {
        var response = await RequestAsync(o => o.SecurityHeaders = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["X-Custom"] = "ok",
        });

        Assert.Equal("ok", response.Headers.GetValues("X-Custom").Single());
        Assert.False(response.Headers.Contains("X-Frame-Options"),
            "Custom SecurityHeaders dictionary should fully replace the defaults, not merge");
    }

    [Fact]
    public async Task SecurityHeaders_can_be_disabled_via_empty_dictionary()
    {
        var response = await RequestAsync(o => o.SecurityHeaders = new Dictionary<string, string>(StringComparer.Ordinal));

        Assert.False(response.Headers.Contains("X-Frame-Options"));
        Assert.False(response.Headers.Contains("X-Content-Type-Options"));
        Assert.False(response.Headers.Contains("Referrer-Policy"));
    }
}
