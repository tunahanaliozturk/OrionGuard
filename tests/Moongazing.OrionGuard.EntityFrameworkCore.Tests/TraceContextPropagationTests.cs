using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.DependencyInjection;
using Moongazing.OrionGuard.Domain.Events;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox;
using Moongazing.OrionGuard.EntityFrameworkCore.Tests.TestFixtures;
using Moongazing.OrionGuard.Testing.DomainEvents;

namespace Moongazing.OrionGuard.EntityFrameworkCore.Tests;

public class TraceContextPropagationTests
{
    [Fact]
    public async Task OutboxRow_RecordsTheCurrentActivityTraceParent()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);

        var dispatcher = new InMemoryDomainEventDispatcher();
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var services = new ServiceCollection();
        services.AddOrionGuardDomainEvents();
        var existing = services.Single(d => d.ServiceType == typeof(IDomainEventDispatcher));
        services.Remove(existing);
        services.AddSingleton<IDomainEventDispatcher>(dispatcher);
        services.AddSingleton(new OrionGuardEfCoreOptions().UseOutbox());
        services.AddScoped<DomainEventCollector>();
        services.AddSingleton(connection);
        services.AddDbContext<TestDbContext>((sp, o) =>
            o.UseSqlite(sp.GetRequiredService<SqliteConnection>())
             .AddInterceptors(new DomainEventSaveChangesInterceptor(sp)));
        await using var sp = services.BuildServiceProvider();

        await using var scope = sp.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        await ctx.Database.EnsureCreatedAsync();

        using var src = new ActivitySource("Test");
        using (var act = src.StartActivity("Test.SaveOrder"))
        {
            var order = new Order(Guid.NewGuid());
            ctx.Orders.Add(order);
            order.Ship();
            await ctx.SaveChangesAsync();

            var row = await ctx.OutboxMessages.AsNoTracking().SingleAsync();
            Assert.Equal(act!.Id, row.TraceParent);
        }
    }

    [Fact]
    public async Task OutboxWorker_RestoresParentTraceContext_WhenDispatchingRow()
    {
        var capturedActivities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == "Moongazing.OrionGuard.DomainEvents" || s.Name == "Test",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = a =>
            {
                if (a.Source.Name == "Moongazing.OrionGuard.DomainEvents")
                {
                    capturedActivities.Add(a);
                }
            },
        };
        ActivitySource.AddActivityListener(listener);

        var dispatcher = new InMemoryDomainEventDispatcher();
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var services = new ServiceCollection();
        services.AddOrionGuardDomainEvents();
        var existing = services.Single(d => d.ServiceType == typeof(IDomainEventDispatcher));
        services.Remove(existing);
        services.AddSingleton<IDomainEventDispatcher>(dispatcher);

        var efOptions = new OrionGuardEfCoreOptions().UseOutbox();
        services.AddSingleton(efOptions);
        services.AddSingleton(efOptions.Outbox);
        services.AddScoped<DomainEventCollector>();
        services.AddSingleton(connection);
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<TestDbContext>());
        services.AddDbContext<TestDbContext>((sp, o) =>
            o.UseSqlite(sp.GetRequiredService<SqliteConnection>())
             .UseOrionGuardDomainEvents(sp));

        await using var sp = services.BuildServiceProvider();
        await using var scope = sp.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        await ctx.Database.EnsureCreatedAsync();

        // Stage a row under a known parent activity context.
        using var src = new ActivitySource("Test");
        string? expectedParentId;
        using (var parent = src.StartActivity("Test.SaveOrder"))
        {
            Assert.NotNull(parent);
            var order = new Order(Guid.NewGuid());
            ctx.Orders.Add(order);
            order.Ship();
            await ctx.SaveChangesAsync();
            expectedParentId = parent.Id;
        }

        var row = await ctx.OutboxMessages.AsNoTracking().SingleAsync();
        Assert.Equal(expectedParentId, row.TraceParent);

        // Now run the worker. It should resume the parent context.
        var worker = new OutboxDispatcherHostedService(
            sp.GetRequiredService<OutboxOptions>(),
            sp.GetRequiredService<IServiceScopeFactory>());
        await worker.ProcessBatchAsync(default);

        // Validate: dispatched, and the worker created an Outbox.Dispatch activity rooted under our parent.
        Assert.Single(dispatcher.Captured);
        var workerActivity = Assert.Single(capturedActivities, a => a.OperationName == "Outbox.Dispatch");
        Assert.Equal(expectedParentId, workerActivity.ParentId);
    }
}
