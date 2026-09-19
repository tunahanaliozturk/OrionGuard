using System.Data.Common;
using System.Diagnostics.Metrics;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.Domain.Events;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox;
using Moongazing.OrionGuard.EntityFrameworkCore.Tests.TestFixtures;

namespace Moongazing.OrionGuard.EntityFrameworkCore.Tests;

/// <summary>
/// With <c>AddDbContextPool</c> the options callback gets the root provider, so one interceptor serves every
/// pooled context in the process; per-save state must still be per context.
/// </summary>
public sealed class DomainEventInterceptorPooledContextTests : IDisposable
{
    private readonly string databaseFile = Path.Combine(Path.GetTempPath(), $"orionguard-pool-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(databaseFile);
    }

    [Fact]
    public async Task AddDbContextPool_SaveThatSucceedsWhileAnotherIsInFlight_DispatchesOnlyItsOwnEvents()
    {
        var dispatcher = new RecordingDispatcher();
        var gate = new SemaphoreSlim(0);
        var failInsert = new FailInsertAfterGateInterceptor(gate);
        var services = new ServiceCollection();
        services.AddDbContextPool<TestDbContext>((provider, o) => o
            .UseSqlite($"Data Source={databaseFile}")
            .AddInterceptors(failInsert)
            .UseOrionGuardDomainEvents(provider));
        services.AddScoped<IDomainEventDispatcher>(_ => dispatcher);
        services.AddOrionGuardEfCore<TestDbContext>(o => o.UseInline());
        // Scope validation off, as in Production: that is where the shared state turned into wrong dispatches.
        await using var serviceProvider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = false });
        await using (var setup = serviceProvider.CreateAsyncScope())
        {
            await setup.ServiceProvider.GetRequiredService<TestDbContext>().Database.EnsureCreatedAsync();
        }

        // Request B: its save is in flight and will fail.
        await using var scopeB = serviceProvider.CreateAsyncScope();
        var dbB = scopeB.ServiceProvider.GetRequiredService<TestDbContext>();
        var orderB = new Order(Guid.NewGuid());
        dbB.Orders.Add(orderB);
        orderB.Cancel();
        var saveB = Task.Run(async () =>
        {
            failInsert.FailThisFlow.Value = true;
            await dbB.SaveChangesAsync();
        });
        await OutboxTestServices.EventuallyAsync(() => Task.FromResult(failInsert.Waiting), TimeSpan.FromSeconds(5));

        // Request A: unrelated, succeeds while B is in flight.
        await using (var scopeA = serviceProvider.CreateAsyncScope())
        {
            var dbA = scopeA.ServiceProvider.GetRequiredService<TestDbContext>();
            var orderA = new Order(Guid.NewGuid());
            dbA.Orders.Add(orderA);
            orderA.Ship();
            await dbA.SaveChangesAsync();
        }

        gate.Release();
        await Assert.ThrowsAsync<DbUpdateException>(() => saveB);

        Assert.IsType<OrderShipped>(Assert.Single(dispatcher.Dispatched));   // A's event only
        Assert.Single(orderB.DomainEvents);                                    // B's stays for B's retry
    }

    [Fact]
    public async Task AddDbContextPool_WithScopeValidation_InlineDispatchRunsInItsOwnScope()
    {
        var scopesSeen = new List<IServiceProvider>();
        var services = new ServiceCollection();
        services.AddDbContextPool<TestDbContext>((provider, o) => o
            .UseSqlite($"Data Source={databaseFile}")
            .UseOrionGuardDomainEvents(provider));
        services.AddScoped<IDomainEventDispatcher>(provider =>
        {
            scopesSeen.Add(provider);
            return new RecordingDispatcher();
        });
        services.AddOrionGuardEfCore<TestDbContext>(o => o.UseInline());
        await using var serviceProvider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        await db.Database.EnsureCreatedAsync();

        var first = new Order(Guid.NewGuid());
        db.Orders.Add(first);
        first.Ship();
        await db.SaveChangesAsync();   // resolving the scoped dispatcher from the root provider would throw here
        var second = new Order(Guid.NewGuid());
        db.Orders.Add(second);
        second.Ship();
        await db.SaveChangesAsync();

        Assert.Equal(2, scopesSeen.Count);
        Assert.NotSame(scopesSeen[0], scopesSeen[1]);   // a scope per dispatch, never one shared process-wide
    }

    [Fact]
    public async Task OutboxMode_EnqueuedRowsPerSave_IsRecordedOnceTheSaveSucceeds()
    {
        var samples = new List<int>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == OutboxDispatcherDiagnostics.MeterName
                && instrument.Name == "orionguard.outbox.enqueued_rows_per_save")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<int>((_, value, _, _) =>
        {
            lock (samples) { samples.Add(value); }
        });
        listener.Start();

        await using var serviceProvider = OutboxTestServices.Build(_ => new RecordingDispatcher());
        await using var scope = serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        var order = new Order(Guid.NewGuid());
        db.Orders.Add(order);
        // Seven events: a count no other test records, so a sample of 7 can only come from this save.
        for (var i = 0; i < 7; i++)
        {
            order.Ship();
        }
        await db.SaveChangesAsync();

        lock (samples) { Assert.Contains(7, samples); }
    }

    /// <summary>Blocks the INSERT of the flow that set <see cref="FailThisFlow"/> until released, then fails it.</summary>
    private sealed class FailInsertAfterGateInterceptor : DbCommandInterceptor
    {
        private readonly SemaphoreSlim gate;
        private volatile bool waiting;

        public FailInsertAfterGateInterceptor(SemaphoreSlim gate) => this.gate = gate;

        public AsyncLocal<bool> FailThisFlow { get; } = new();

        public bool Waiting => waiting;

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            await FailIfFlaggedAsync(command, cancellationToken);
            return result;
        }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            await FailIfFlaggedAsync(command, cancellationToken);
            return result;
        }

        private async Task FailIfFlaggedAsync(DbCommand command, CancellationToken cancellationToken)
        {
            if (!FailThisFlow.Value || !command.CommandText.Contains("INSERT", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            waiting = true;
            await gate.WaitAsync(cancellationToken);
            throw new InvalidOperationException("simulated constraint violation in request B");
        }
    }
}
