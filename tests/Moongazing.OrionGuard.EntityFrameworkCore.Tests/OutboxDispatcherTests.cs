using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.DependencyInjection;
using Moongazing.OrionGuard.Domain.Events;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox;
using Moongazing.OrionGuard.EntityFrameworkCore.Tests.TestFixtures;
using Moongazing.OrionGuard.Testing.DomainEvents;

namespace Moongazing.OrionGuard.EntityFrameworkCore.Tests;

public class OutboxDispatcherTests
{
    private static ServiceProvider BuildSp(IDomainEventDispatcher dispatcher, OutboxOptions? opts = null)
    {
        var resolved = opts ?? new OutboxOptions { PollingInterval = TimeSpan.FromMilliseconds(50), BatchSize = 10, MaxRetries = 2 };
        var services = new ServiceCollection();
        services.AddOrionGuardDomainEvents();
        var existing = services.Single(d => d.ServiceType == typeof(IDomainEventDispatcher));
        services.Remove(existing);
        services.AddSingleton(dispatcher);

        var efCoreOptions = new OrionGuardEfCoreOptions().UseOutbox(o =>
        {
            o.PollingInterval = resolved.PollingInterval;
            o.BatchSize = resolved.BatchSize;
            o.MaxRetries = resolved.MaxRetries;
            o.TableName = resolved.TableName;
        });
        services.AddSingleton(efCoreOptions);
        services.AddSingleton(efCoreOptions.Outbox);
        services.AddScoped<DomainEventCollector>();
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<TestDbContext>());

        // Share a single SqliteConnection across all scopes so the in-memory database survives
        // the worker's CreateAsyncScope() (each :memory: SqliteConnection is its own database).
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        services.AddSingleton(connection);
        services.AddDbContext<TestDbContext>((sp, o) =>
            o.UseSqlite(sp.GetRequiredService<SqliteConnection>())
             .AddInterceptors(new DomainEventSaveChangesInterceptor(sp)));
        return services.BuildServiceProvider();
    }

    private static async Task SeedOutboxRowAsync(TestDbContext ctx, IDomainEvent evt)
    {
        ctx.OutboxMessages.Add(new OutboxMessage
        {
            EventType = evt.GetType().AssemblyQualifiedName!,
            Payload = JsonSerializer.Serialize(evt, evt.GetType()),
            OccurredOnUtc = evt.OccurredOnUtc,
        });
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task ProcessBatch_DispatchesUnprocessedRowsAndMarksThemProcessed()
    {
        var dispatcher = new InMemoryDomainEventDispatcher();
        await using var sp = BuildSp(dispatcher);
        await using var scope = sp.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        await ctx.Database.OpenConnectionAsync();
        await ctx.Database.EnsureCreatedAsync();

        await SeedOutboxRowAsync(ctx, new OrderShipped(Guid.NewGuid()));

        var worker = new OutboxDispatcherHostedService(
            sp.GetRequiredService<OutboxOptions>(),
            sp.GetRequiredService<IServiceScopeFactory>());

        await worker.ProcessBatchAsync(default);

        Assert.Single(dispatcher.Captured);
        var row = await ctx.OutboxMessages.AsNoTracking().SingleAsync();
        Assert.NotNull(row.ProcessedOnUtc);
    }

    [Fact]
    public async Task ProcessBatch_OnHandlerThrow_IncrementsRetryAndRecordsError()
    {
        var dispatcher = new ThrowingDispatcher();
        await using var sp = BuildSp(dispatcher);
        await using var scope = sp.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        await ctx.Database.OpenConnectionAsync();
        await ctx.Database.EnsureCreatedAsync();

        await SeedOutboxRowAsync(ctx, new OrderShipped(Guid.NewGuid()));

        var worker = new OutboxDispatcherHostedService(
            sp.GetRequiredService<OutboxOptions>(),
            sp.GetRequiredService<IServiceScopeFactory>());

        await worker.ProcessBatchAsync(default);

        var row = await ctx.OutboxMessages.AsNoTracking().SingleAsync();
        Assert.Equal(1, row.RetryCount);
        Assert.NotNull(row.Error);
        Assert.Null(row.ProcessedOnUtc);
    }

    [Fact]
    public async Task ProcessBatch_HonoursCancellation_StopsBeforeDispatch()
    {
        var dispatcher = new InMemoryDomainEventDispatcher();
        await using var sp = BuildSp(dispatcher);
        await using var scope = sp.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        await ctx.Database.OpenConnectionAsync();
        await ctx.Database.EnsureCreatedAsync();

        await SeedOutboxRowAsync(ctx, new OrderShipped(Guid.NewGuid()));

        var worker = new OutboxDispatcherHostedService(
            sp.GetRequiredService<OutboxOptions>(),
            sp.GetRequiredService<IServiceScopeFactory>());

        using var cts = new CancellationTokenSource();
        cts.Cancel();   // pre-cancelled

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => worker.ProcessBatchAsync(cts.Token));

        // The cancelled call must not have dispatched the row.
        Assert.Empty(dispatcher.Captured);
        var row = await ctx.OutboxMessages.AsNoTracking().SingleAsync();
        Assert.Null(row.ProcessedOnUtc);
        Assert.Equal(0, row.RetryCount);
    }

    [Fact]
    public async Task ProcessBatch_AfterMaxRetries_DeadLettersTheRow()
    {
        var dispatcher = new ThrowingDispatcher();
        var opts = new OutboxOptions { MaxRetries = 2, BatchSize = 10, PollingInterval = TimeSpan.FromMilliseconds(50) };
        await using var sp = BuildSp(dispatcher, opts);
        await using var scope = sp.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        await ctx.Database.OpenConnectionAsync();
        await ctx.Database.EnsureCreatedAsync();

        await SeedOutboxRowAsync(ctx, new OrderShipped(Guid.NewGuid()));
        var worker = new OutboxDispatcherHostedService(opts, sp.GetRequiredService<IServiceScopeFactory>());

        await worker.ProcessBatchAsync(default);
        await worker.ProcessBatchAsync(default);

        var row = await ctx.OutboxMessages.AsNoTracking().SingleAsync();
        Assert.Equal(2, row.RetryCount);
        Assert.NotNull(row.ProcessedOnUtc);   // dead-lettered
    }

    [Fact]
    public async Task ProcessBatch_PersistsEachRowIndependently()
    {
        var dispatcher = new InMemoryDomainEventDispatcher();
        await using var sp = BuildSp(dispatcher);
        await using var scope = sp.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        await ctx.Database.OpenConnectionAsync();
        await ctx.Database.EnsureCreatedAsync();

        // Seed three rows.
        await SeedOutboxRowAsync(ctx, new OrderShipped(Guid.NewGuid()));
        await SeedOutboxRowAsync(ctx, new OrderShipped(Guid.NewGuid()));
        await SeedOutboxRowAsync(ctx, new OrderShipped(Guid.NewGuid()));

        var worker = new OutboxDispatcherHostedService(
            sp.GetRequiredService<OutboxOptions>(),
            sp.GetRequiredService<IServiceScopeFactory>());

        await worker.ProcessBatchAsync(default);

        // All three rows should be processed individually.
        var processed = await ctx.OutboxMessages.AsNoTracking()
            .CountAsync(m => m.ProcessedOnUtc != null);
        Assert.Equal(3, processed);
        Assert.Equal(3, dispatcher.Captured.Count);
    }

    [Fact]
    public async Task ProcessBatch_UnresolvableEventType_DeadLettersImmediately()
    {
        var dispatcher = new InMemoryDomainEventDispatcher();
        await using var sp = BuildSp(dispatcher);
        await using var scope = sp.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        await ctx.Database.OpenConnectionAsync();
        await ctx.Database.EnsureCreatedAsync();

        ctx.OutboxMessages.Add(new OutboxMessage
        {
            EventType = "SomeApp.Domain.Events.OrderShipped, NonExistentAssembly, Version=1.0.0.0",
            Payload = "{}",
            OccurredOnUtc = DateTime.UtcNow,
        });
        await ctx.SaveChangesAsync();

        var worker = new OutboxDispatcherHostedService(
            sp.GetRequiredService<OutboxOptions>(),
            sp.GetRequiredService<IServiceScopeFactory>());

        await worker.ProcessBatchAsync(default);

        var row = await ctx.OutboxMessages.AsNoTracking().SingleAsync();
        Assert.Equal(0, row.RetryCount);     // dead-lettered without retry
        Assert.NotNull(row.ProcessedOnUtc);   // dead-lettered
        Assert.NotNull(row.Error);
        Assert.StartsWith("TYPE_NOT_FOUND:", row.Error);
        Assert.Empty(dispatcher.Captured);    // no dispatch attempted
    }

    [Fact]
    public void OutboxOptions_RejectsNonPositivePollingInterval()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new OutboxOptions { PollingInterval = TimeSpan.Zero });
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new OutboxOptions { PollingInterval = TimeSpan.FromSeconds(-1) });
    }

    [Fact]
    public void OutboxOptions_RejectsNonPositiveBatchSize()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new OutboxOptions { BatchSize = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new OutboxOptions { BatchSize = -1 });
    }

    [Fact]
    public void OutboxOptions_RejectsMaxRetriesBelowOne()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new OutboxOptions { MaxRetries = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new OutboxOptions { MaxRetries = 0 });
        var ok = new OutboxOptions { MaxRetries = 1 };
        Assert.Equal(1, ok.MaxRetries);
    }

    [Fact]
    public void OutboxOptions_RejectsNullOrWhitespaceTableName()
    {
        Assert.Throws<ArgumentException>(() => new OutboxOptions { TableName = null! });
        Assert.Throws<ArgumentException>(() => new OutboxOptions { TableName = "" });
        Assert.Throws<ArgumentException>(() => new OutboxOptions { TableName = "   " });
    }

    private sealed class ThrowingDispatcher : IDomainEventDispatcher
    {
        public Task DispatchAsync(IDomainEvent @event, CancellationToken ct = default) => throw new InvalidOperationException("boom");
        public Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken ct = default) => throw new InvalidOperationException("boom");
    }
}
