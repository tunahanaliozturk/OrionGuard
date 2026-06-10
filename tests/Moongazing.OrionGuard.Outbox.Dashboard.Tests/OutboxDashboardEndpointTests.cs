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
                    s.AddDbContext<TestDbContext>(opts => opts.UseInMemoryDatabase(dbName, inMemoryRoot).ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)));
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
    public async Task Failed_endpoint_returns_failed_and_dead_lettered_rows_excluding_clean_successes()
    {
        await SeedAsync(new[]
        {
            Row(retryCount: 0),                                                              // below threshold
            Row(retryCount: 2),                                                              // below threshold
            Row(retryCount: 3),                                                              // failed (not yet dead-lettered)
            Row(retryCount: 5),                                                              // failed
            Row(retryCount: 4, processedOnUtc: DateTime.UtcNow),                              // dead-lettered (still has Error) -> INCLUDED
            Row(retryCount: 4, processedOnUtc: DateTime.UtcNow, error: null),                 // clean success with retries -> excluded
        });

        var response = await client.GetAsync("/_orion/outbox/failed");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(3, body.GetProperty("total").GetInt32());
        Assert.Equal(3, body.GetProperty("items").GetArrayLength());
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

    [Fact]
    public async Task Failed_endpoint_returns_pagination_metadata()
    {
        await SeedAsync(Enumerable.Range(0, 25).Select(_ => Row(retryCount: 3)));

        var body = await client.GetFromJsonAsync<JsonElement>("/_orion/outbox/failed?page=2&size=10");

        Assert.Equal(25, body.GetProperty("total").GetInt32());
        Assert.Equal(3, body.GetProperty("totalPages").GetInt32());
        Assert.True(body.GetProperty("hasNextPage").GetBoolean());
        Assert.True(body.GetProperty("hasPreviousPage").GetBoolean());
    }

    [Fact]
    public async Task Failed_endpoint_sort_NewestFirst_orders_descending_by_OccurredOnUtc()
    {
        var anchor = DateTime.UtcNow;
        await SeedAsync(new[]
        {
            new OutboxMessage { EventType = "T", Payload = "{}", OccurredOnUtc = anchor.AddMinutes(-30), RetryCount = 3, Error = "boom1" },
            new OutboxMessage { EventType = "T", Payload = "{}", OccurredOnUtc = anchor.AddMinutes(-10), RetryCount = 3, Error = "boom2" },
            new OutboxMessage { EventType = "T", Payload = "{}", OccurredOnUtc = anchor.AddMinutes(-20), RetryCount = 3, Error = "boom3" },
        });

        var body = await client.GetFromJsonAsync<JsonElement>("/_orion/outbox/failed?sort=NewestFirst");

        var items = body.GetProperty("items");
        Assert.Equal(3, items.GetArrayLength());
        for (var i = 0; i < items.GetArrayLength() - 1; i++)
        {
            var current = items[i].GetProperty("occurredOnUtc").GetDateTime();
            var next = items[i + 1].GetProperty("occurredOnUtc").GetDateTime();
            Assert.True(current >= next, "rows must be in descending OccurredOnUtc order");
        }
        Assert.Equal("NewestFirst", body.GetProperty("sort").GetString());
    }

    [Fact]
    public async Task Failed_endpoint_sort_MostRetries_orders_descending_by_RetryCount()
    {
        await SeedAsync(new[]
        {
            new OutboxMessage { EventType = "T", Payload = "{}", OccurredOnUtc = DateTime.UtcNow, RetryCount = 5, Error = "x" },
            new OutboxMessage { EventType = "T", Payload = "{}", OccurredOnUtc = DateTime.UtcNow, RetryCount = 9, Error = "x" },
            new OutboxMessage { EventType = "T", Payload = "{}", OccurredOnUtc = DateTime.UtcNow, RetryCount = 3, Error = "x" },
        });

        var body = await client.GetFromJsonAsync<JsonElement>("/_orion/outbox/failed?sort=MostRetries");

        var items = body.GetProperty("items");
        Assert.Equal(9, items[0].GetProperty("retryCount").GetInt32());
        Assert.Equal(5, items[1].GetProperty("retryCount").GetInt32());
        Assert.Equal(3, items[2].GetProperty("retryCount").GetInt32());
    }

    [Fact]
    public async Task Failed_endpoint_numeric_sort_outside_enum_range_falls_back_to_default()
    {
        await SeedAsync(new[] { Row(retryCount: 3) });

        // ?sort=99 must NOT be accepted by Enum.TryParse + Enum.IsDefined - the response
        // would otherwise echo "99" as the sort axis even though the rows came out in the
        // default order.
        var body = await client.GetFromJsonAsync<JsonElement>("/_orion/outbox/failed?sort=99");

        Assert.Equal("OldestFirst", body.GetProperty("sort").GetString());
    }

    [Fact]
    public async Task Failed_endpoint_invalid_sort_falls_back_to_default()
    {
        await SeedAsync(new[] { Row(retryCount: 3) });

        var body = await client.GetFromJsonAsync<JsonElement>("/_orion/outbox/failed?sort=garbage");

        // Default is OldestFirst; the response echoes the resolved sort regardless.
        Assert.Equal("OldestFirst", body.GetProperty("sort").GetString());
    }

    [Fact]
    public async Task Replay_endpoint_clears_retry_count_and_error()
    {
        var failing = Row(retryCount: 5);
        await SeedAsync(new[] { failing });

        var response = await client.PostAsync($"/_orion/outbox/{failing.Id}/replay", content: null);

        response.EnsureSuccessStatusCode();
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        var fresh = await db.Set<OutboxMessage>().AsNoTracking().FirstAsync(x => x.Id == failing.Id);
        Assert.Equal(0, fresh.RetryCount);
        Assert.Null(fresh.Error);
        Assert.Null(fresh.ProcessedOnUtc);
    }

    [Fact]
    public async Task Replay_endpoint_rejects_cleanly_processed_rows_with_409()
    {
        // ProcessedOnUtc set + Error null => the row dispatched successfully. Replay would
        // re-deliver a clean event so the endpoint must refuse.
        var successful = Row(retryCount: 1, processedOnUtc: DateTime.UtcNow, error: null);
        await SeedAsync(new[] { successful });

        var response = await client.PostAsync($"/_orion/outbox/{successful.Id}/replay", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        var fresh = await db.Set<OutboxMessage>().AsNoTracking().FirstAsync(x => x.Id == successful.Id);
        Assert.NotNull(fresh.ProcessedOnUtc); // Must NOT have been cleared.
    }

    [Fact]
    public async Task Replay_endpoint_returns_404_for_unknown_id()
    {
        var response = await client.PostAsync($"/_orion/outbox/{Guid.NewGuid()}/replay", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Discard_endpoint_marks_row_processed_without_re_dispatch()
    {
        var failing = Row(retryCount: 5);
        await SeedAsync(new[] { failing });

        var response = await client.PostAsync($"/_orion/outbox/{failing.Id}/discard", content: null);

        response.EnsureSuccessStatusCode();
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        var fresh = await db.Set<OutboxMessage>().AsNoTracking().FirstAsync(x => x.Id == failing.Id);
        Assert.NotNull(fresh.ProcessedOnUtc);
        // Error and RetryCount stay intact so future operators see the history.
        Assert.Equal(5, fresh.RetryCount);
        Assert.NotNull(fresh.Error);
    }

    [Fact]
    public async Task Discard_endpoint_is_idempotent_for_already_processed_rows()
    {
        var processed = Row(retryCount: 3, processedOnUtc: DateTime.UtcNow);
        await SeedAsync(new[] { processed });

        var response = await client.PostAsync($"/_orion/outbox/{processed.Id}/discard", content: null);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("already processed", body.GetProperty("note").GetString()!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Discard_endpoint_returns_404_for_unknown_id()
    {
        var response = await client.PostAsync($"/_orion/outbox/{Guid.NewGuid()}/discard", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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

public sealed class OutboxDashboardMutationHookTests : IAsyncLifetime
{
    private readonly Microsoft.EntityFrameworkCore.Storage.InMemoryDatabaseRoot inMemoryRoot = new();
    private IHost host = default!;
    private HttpClient client = default!;
    private List<OutboxMutationEvent> events = default!;

    public async Task InitializeAsync()
    {
        events = new List<OutboxMutationEvent>();
        var dbName = "mut-hook-" + Guid.NewGuid().ToString("N");
        host = await new HostBuilder()
            .ConfigureWebHost(builder =>
            {
                builder.UseTestServer();
                builder.ConfigureServices(s =>
                {
                    s.AddRouting();
                    s.AddDbContext<MutHookCtx>(opts => opts.UseInMemoryDatabase(dbName, inMemoryRoot).ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)));
                });
                builder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(e => e.MapOutboxDashboard<MutHookCtx>(o =>
                    {
                        o.AllowAnonymous = true;
                        o.OnMutation = evt =>
                        {
                            events.Add(evt);
                            return Task.CompletedTask;
                        };
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

    [Fact]
    public async Task OnMutation_fires_after_successful_replay()
    {
        var id = Guid.NewGuid();
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MutHookCtx>();
            db.Add(new OutboxMessage
            {
                Id = id, EventType = "T", Payload = "{}", OccurredOnUtc = DateTime.UtcNow,
                RetryCount = 4, Error = "boom",
            });
            await db.SaveChangesAsync();
        }

        var response = await client.PostAsync($"/_orion/outbox/{id}/replay", content: null);
        response.EnsureSuccessStatusCode();

        Assert.Single(events);
        Assert.Equal("replay", events[0].Action);
        Assert.Equal(id, events[0].OutboxMessageId);
    }

    [Fact]
    public async Task OnMutation_fires_after_successful_discard()
    {
        var id = Guid.NewGuid();
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MutHookCtx>();
            db.Add(new OutboxMessage
            {
                Id = id, EventType = "T", Payload = "{}", OccurredOnUtc = DateTime.UtcNow,
                RetryCount = 4, Error = "boom",
            });
            await db.SaveChangesAsync();
        }

        var response = await client.PostAsync($"/_orion/outbox/{id}/discard", content: null);
        response.EnsureSuccessStatusCode();

        Assert.Single(events);
        Assert.Equal("discard", events[0].Action);
        Assert.Equal(id, events[0].OutboxMessageId);
    }

    [Fact]
    public async Task OnMutation_does_NOT_fire_when_replay_targets_unknown_id()
    {
        var response = await client.PostAsync($"/_orion/outbox/{Guid.NewGuid()}/replay", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(events);
    }

    [Fact]
    public async Task OnMutation_does_NOT_fire_when_discard_targets_already_processed_row()
    {
        var id = Guid.NewGuid();
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MutHookCtx>();
            db.Add(new OutboxMessage
            {
                Id = id, EventType = "T", Payload = "{}", OccurredOnUtc = DateTime.UtcNow,
                RetryCount = 3, Error = "boom", ProcessedOnUtc = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var response = await client.PostAsync($"/_orion/outbox/{id}/discard", content: null);
        response.EnsureSuccessStatusCode();

        Assert.Empty(events);
    }

    private sealed class MutHookCtx : DbContext
    {
        public MutHookCtx(DbContextOptions<MutHookCtx> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<OutboxMessage>().ToTable("OutboxMessages").HasKey(x => x.Id);
    }
}

public sealed class OutboxDashboardReadOnlyModeTests : IAsyncLifetime
{
    private readonly Microsoft.EntityFrameworkCore.Storage.InMemoryDatabaseRoot inMemoryRoot = new();
    private IHost host = default!;
    private HttpClient client = default!;

    public async Task InitializeAsync()
    {
        var dbName = "readonly-" + Guid.NewGuid().ToString("N");
        host = await new HostBuilder()
            .ConfigureWebHost(builder =>
            {
                builder.UseTestServer();
                builder.ConfigureServices(s =>
                {
                    s.AddRouting();
                    s.AddDbContext<ReadOnlyCtx>(opts => opts.UseInMemoryDatabase(dbName, inMemoryRoot).ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)));
                });
                builder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(e => e.MapOutboxDashboard<ReadOnlyCtx>(o =>
                    {
                        o.AllowAnonymous = true;
                        o.EnableMutations = false;
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

    [Fact]
    public async Task EnableMutations_false_does_NOT_register_replay_or_discard_endpoints()
    {
        var replay = await client.PostAsync($"/_orion/outbox/{Guid.NewGuid()}/replay", content: null);
        var discard = await client.PostAsync($"/_orion/outbox/{Guid.NewGuid()}/discard", content: null);

        Assert.Equal(HttpStatusCode.NotFound, replay.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, discard.StatusCode);
    }

    [Fact]
    public async Task EnableMutations_false_still_serves_the_read_listing()
    {
        var response = await client.GetAsync("/_orion/outbox/failed");

        response.EnsureSuccessStatusCode();
    }

    private sealed class ReadOnlyCtx : DbContext
    {
        public ReadOnlyCtx(DbContextOptions<ReadOnlyCtx> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<OutboxMessage>().ToTable("OutboxMessages").HasKey(x => x.Id);
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
                    s.AddDbContext<DummyDbContext>(opts => opts.UseInMemoryDatabase("opts-test").ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)));
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
                    s.AddDbContext<DummyDbContext>(opts => opts.UseInMemoryDatabase("opts-" + Guid.NewGuid()).ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)));
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
