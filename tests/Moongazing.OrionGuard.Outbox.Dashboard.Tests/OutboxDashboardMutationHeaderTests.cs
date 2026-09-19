namespace Moongazing.OrionGuard.Outbox.Dashboard.Tests;

using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox;

/// <summary>
/// Replay and discard take no body, so without a required custom header a page on another origin could send
/// them as simple cross-origin POSTs carrying the operator's cookies.
/// </summary>
public sealed class OutboxDashboardMutationHeaderTests
{
    private const string DefaultHeader = "X-OrionGuard-Dashboard";

    [Fact]
    public async Task Replay_without_the_mutation_header_is_rejected_and_leaves_the_row_failed()
    {
        await using var dashboard = await DashboardHost.StartAsync();
        var row = await dashboard.SeedFailedRowAsync();

        var response = await dashboard.Client.PostAsync($"/_orion/outbox/{row.Id}/replay", content: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("missing-mutation-header", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var stored = await dashboard.ReadAsync(row.Id);
        Assert.Equal(3, stored.RetryCount);
        Assert.Equal("boom", stored.Error);
        Assert.Equal(0, dashboard.MutationEvents);
    }

    [Fact]
    public async Task Discard_without_the_mutation_header_is_rejected_and_leaves_the_row_unprocessed()
    {
        await using var dashboard = await DashboardHost.StartAsync();
        var row = await dashboard.SeedFailedRowAsync();

        var response = await dashboard.Client.PostAsync($"/_orion/outbox/{row.Id}/discard", content: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null((await dashboard.ReadAsync(row.Id)).ProcessedOnUtc);
        Assert.Equal(0, dashboard.MutationEvents);
    }

    [Fact]
    public async Task Mutation_header_with_an_empty_value_is_rejected()
    {
        await using var dashboard = await DashboardHost.StartAsync();
        var row = await dashboard.SeedFailedRowAsync();

        var response = await dashboard.Client.SendAsync(Post($"/_orion/outbox/{row.Id}/replay", DefaultHeader, ""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(3, (await dashboard.ReadAsync(row.Id)).RetryCount);
    }

    [Fact]
    public async Task Replay_and_discard_with_the_mutation_header_succeed()
    {
        await using var dashboard = await DashboardHost.StartAsync();
        var replayed = await dashboard.SeedFailedRowAsync();
        var discarded = await dashboard.SeedFailedRowAsync();

        var replay = await dashboard.Client.SendAsync(Post($"/_orion/outbox/{replayed.Id}/replay", DefaultHeader, "1"));
        var discard = await dashboard.Client.SendAsync(Post($"/_orion/outbox/{discarded.Id}/discard", DefaultHeader, "1"));

        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(HttpStatusCode.OK, discard.StatusCode);
        Assert.Equal(0, (await dashboard.ReadAsync(replayed.Id)).RetryCount);
        Assert.NotNull((await dashboard.ReadAsync(discarded.Id)).ProcessedOnUtc);
        Assert.Equal(2, dashboard.MutationEvents);
    }

    [Fact]
    public async Task Mutation_header_name_is_configurable()
    {
        await using var dashboard = await DashboardHost.StartAsync(o => o.MutationHeaderName = "X-Outbox-Ops");
        var row = await dashboard.SeedFailedRowAsync();

        var withDefaultName = await dashboard.Client.SendAsync(Post($"/_orion/outbox/{row.Id}/replay", DefaultHeader, "1"));
        var withConfiguredName = await dashboard.Client.SendAsync(Post($"/_orion/outbox/{row.Id}/replay", "X-Outbox-Ops", "1"));

        Assert.Equal(HttpStatusCode.BadRequest, withDefaultName.StatusCode);
        Assert.Equal(HttpStatusCode.OK, withConfiguredName.StatusCode);
    }

    [Fact]
    public async Task RequireMutationHeader_false_accepts_a_mutation_without_the_header()
    {
        await using var dashboard = await DashboardHost.StartAsync(o => o.RequireMutationHeader = false);
        var row = await dashboard.SeedFailedRowAsync();

        var response = await dashboard.Client.PostAsync($"/_orion/outbox/{row.Id}/replay", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, (await dashboard.ReadAsync(row.Id)).RetryCount);
    }

    [Fact]
    public async Task Read_endpoints_do_not_require_the_mutation_header()
    {
        await using var dashboard = await DashboardHost.StartAsync();

        var listing = await dashboard.Client.GetAsync("/_orion/outbox/failed");
        var cursor = await dashboard.Client.GetAsync("/_orion/outbox/failed/cursor");

        Assert.Equal(HttpStatusCode.OK, listing.StatusCode);
        Assert.Equal(HttpStatusCode.OK, cursor.StatusCode);
    }

    private static HttpRequestMessage Post(string path, string headerName, string headerValue)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add(headerName, headerValue);
        return request;
    }

    private sealed class DashboardHost : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly IHost host;
        private int mutationEvents;

        private DashboardHost(SqliteConnection connection, IHost host)
        {
            this.connection = connection;
            this.host = host;
            Client = host.GetTestClient();
        }

        public HttpClient Client { get; }

        public int MutationEvents => Volatile.Read(ref mutationEvents);

        // SQLite rather than InMemory: replay and discard are conditional UPDATEs, which only a relational provider runs.
        public static async Task<DashboardHost> StartAsync(Action<OutboxDashboardOptions>? configure = null)
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();
            DashboardHost? dashboard = null;
            var host = await new HostBuilder()
                .ConfigureWebHost(builder =>
                {
                    builder.UseTestServer();
                    builder.ConfigureServices(services =>
                    {
                        services.AddRouting();
                        services.AddDbContext<HeaderTestDbContext>(options => options.UseSqlite(connection));
                    });
                    builder.Configure(app =>
                    {
                        app.UseRouting();
                        app.UseEndpoints(endpoints => endpoints.MapOutboxDashboard<HeaderTestDbContext>(o =>
                        {
                            o.AllowAnonymous = true;
                            o.OnMutation = _ =>
                            {
                                Interlocked.Increment(ref dashboard!.mutationEvents);
                                return Task.CompletedTask;
                            };
                            configure?.Invoke(o);
                        }));
                    });
                })
                .StartAsync();

            dashboard = new DashboardHost(connection, host);
            using var scope = host.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<HeaderTestDbContext>().Database.EnsureCreatedAsync();
            return dashboard;
        }

        public async Task<OutboxMessage> SeedFailedRowAsync()
        {
            var row = new OutboxMessage
            {
                EventType = "Demo.Event, Demo",
                Payload = "{}",
                OccurredOnUtc = DateTime.UtcNow,
                Error = "boom",
                RetryCount = 3,
            };
            using var scope = host.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<HeaderTestDbContext>();
            db.Set<OutboxMessage>().Add(row);
            await db.SaveChangesAsync();
            return row;
        }

        public async Task<OutboxMessage> ReadAsync(Guid id)
        {
            using var scope = host.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<HeaderTestDbContext>()
                .Set<OutboxMessage>().AsNoTracking().SingleAsync(m => m.Id == id);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await host.StopAsync();
            host.Dispose();
            await connection.DisposeAsync();
        }
    }

    private sealed class HeaderTestDbContext : DbContext
    {
        public HeaderTestDbContext(DbContextOptions<HeaderTestDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<OutboxMessage>().ToTable("OutboxMessages").HasKey(x => x.Id);
    }
}
