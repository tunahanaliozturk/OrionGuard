namespace Moongazing.OrionGuard.Outbox.Dashboard.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox;
using Moongazing.OrionGuard.Outbox.Dashboard;

public sealed class OutboxDashboardEndpointTests : IAsyncLifetime
{
    private IHost host = default!;
    private HttpClient client = default!;

    private readonly Microsoft.EntityFrameworkCore.Storage.InMemoryDatabaseRoot inMemoryRoot = new();

    public async Task InitializeAsync()
    {
        var dbName = "dashboard-tests-" + Guid.NewGuid().ToString("N");
        host = await new HostBuilder()
            .ConfigureWebHost(builder =>
            {
                builder.UseTestServer();
                builder.ConfigureServices(s =>
                {
                    s.AddRouting();
                    s.AddDbContext<TestDbContext>(opts => opts.UseInMemoryDatabase(dbName, inMemoryRoot));
                });
                builder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(e => e.MapOutboxDashboard<TestDbContext>(o =>
                    {
                        o.AllowAnonymous = true;
                        o.FailedRetryThreshold = 3;
                    }));
                });
            })
            .StartAsync();
        client = host.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        client.Dispose();
        await host.StopAsync();
        host.Dispose();
    }

    private async Task SeedAsync(IEnumerable<OutboxMessage> rows)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        db.Set<OutboxMessage>().AddRange(rows);
        await db.SaveChangesAsync();
    }

    private static OutboxMessage Row(int retryCount, DateTime? processedOnUtc = null, string? error = "boom")
        => new()
        {
            EventType = "Demo.Event, Demo",
            Payload = "{}",
            OccurredOnUtc = DateTime.UtcNow.AddMinutes(-retryCount),
            ProcessedOnUtc = processedOnUtc,
            Error = error,
            RetryCount = retryCount,
            CorrelationId = "corr-" + retryCount,
        };

    [Fact]
    public async Task Failed_endpoint_returns_only_unprocessed_rows_at_or_above_threshold()
    {
        await SeedAsync(new[]
        {
            Row(retryCount: 0),                                        // below threshold
            Row(retryCount: 2),                                        // below threshold
            Row(retryCount: 3),                                        // failed
            Row(retryCount: 5),                                        // failed
            Row(retryCount: 4, processedOnUtc: DateTime.UtcNow),        // processed -> excluded
        });

        var response = await client.GetAsync("/_orion/outbox/failed");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(2, body.GetProperty("total").GetInt32());
        Assert.Equal(2, body.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Failed_endpoint_paginates_with_page_and_size_query()
    {
        await SeedAsync(Enumerable.Range(0, 30).Select(_ => Row(retryCount: 3)));

        var page2 = await client.GetFromJsonAsync<JsonElement>("/_orion/outbox/failed?page=2&size=10");

        Assert.Equal(30, page2.GetProperty("total").GetInt32());
        Assert.Equal(10, page2.GetProperty("items").GetArrayLength());
        Assert.Equal(2, page2.GetProperty("page").GetInt32());
        Assert.Equal(10, page2.GetProperty("size").GetInt32());
    }

    [Fact]
    public async Task Failed_endpoint_clamps_size_to_MaxPageSize()
    {
        await SeedAsync(Enumerable.Range(0, 5).Select(_ => Row(retryCount: 3)));

        // Default MaxPageSize is 100; request 99999 and verify it does NOT come through as-is.
        var response = await client.GetFromJsonAsync<JsonElement>("/_orion/outbox/failed?size=99999");

        Assert.Equal(100, response.GetProperty("size").GetInt32());
    }

    [Fact]
    public async Task Failed_endpoint_truncates_error_text()
    {
        var longError = new string('x', 4096);
        await SeedAsync(new[]
        {
            new OutboxMessage
            {
                EventType = "T",
                Payload = "{}",
                OccurredOnUtc = DateTime.UtcNow,
                RetryCount = 3,
                Error = longError,
            },
        });

        var body = await client.GetFromJsonAsync<JsonElement>("/_orion/outbox/failed");
        var item = body.GetProperty("items")[0];

        Assert.Equal(1024, item.GetProperty("error").GetString()!.Length);
    }

    [Fact]
    public async Task Failed_endpoint_excludes_payload()
    {
        await SeedAsync(new[] { Row(retryCount: 3) });

        var raw = await client.GetStringAsync("/_orion/outbox/failed");

        Assert.DoesNotContain("payload", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Failed_endpoint_returns_empty_when_no_rows()
    {
        var body = await client.GetFromJsonAsync<JsonElement>("/_orion/outbox/failed");

        Assert.Equal(0, body.GetProperty("total").GetInt32());
        Assert.Equal(0, body.GetProperty("items").GetArrayLength());
    }

    private sealed class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<OutboxMessage>(b =>
            {
                b.ToTable("OutboxMessages");
                b.HasKey(x => x.Id);
            });
        }
    }
}

public sealed class OutboxDashboardConfigurationTests
{
    [Fact]
    public void MapOutboxDashboard_throws_on_empty_route_prefix()
    {
        using var host = new HostBuilder()
            .ConfigureWebHost(builder =>
            {
                builder.UseTestServer();
                builder.ConfigureServices(s =>
                {
                    s.AddRouting();
                    s.AddDbContext<DummyDbContext>(opts => opts.UseInMemoryDatabase("opts-test"));
                });
                builder.Configure(app =>
                {
                    app.UseRouting();
                    Assert.Throws<InvalidOperationException>(() =>
                        app.UseEndpoints(e => e.MapOutboxDashboard<DummyDbContext>(o => o.RoutePrefix = " ")));
                });
            })
            .Start();
    }

    [Fact]
    public void MapOutboxDashboard_throws_on_invalid_page_sizes()
    {
        Assert.Throws<InvalidOperationException>(() => Build(o => o.MaxPageSize = 0));
        Assert.Throws<InvalidOperationException>(() => Build(o => o.DefaultPageSize = 0));
        Assert.Throws<InvalidOperationException>(() => Build(o => o.FailedRetryThreshold = 0));
        Assert.Throws<InvalidOperationException>(() => Build(o => o.ErrorTruncationLength = -1));
    }

    private static IHost Build(Action<OutboxDashboardOptions> configure) =>
        new HostBuilder()
            .ConfigureWebHost(builder =>
            {
                builder.UseTestServer();
                builder.ConfigureServices(s =>
                {
                    s.AddRouting();
                    s.AddDbContext<DummyDbContext>(opts => opts.UseInMemoryDatabase("opts-" + Guid.NewGuid()));
                });
                builder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(e => e.MapOutboxDashboard<DummyDbContext>(configure));
                });
            })
            .Start();

    private sealed class DummyDbContext : DbContext
    {
        public DummyDbContext(DbContextOptions<DummyDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<OutboxMessage>().ToTable("X").HasKey(x => x.Id);
    }
}
