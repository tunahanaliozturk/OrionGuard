using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Archival;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Locking;

namespace Moongazing.OrionGuard.EntityFrameworkCore.Tests.Outbox.Archival;

public sealed class ArchivalTestFixture : IAsyncDisposable
{
    public SqliteConnection Connection { get; }
    public IServiceProvider Services { get; }

    public ArchivalTestFixture()
    {
        Connection = new SqliteConnection("Filename=:memory:");
        Connection.Open();

        var services = new ServiceCollection();
        services.AddDbContext<ArchivalTestDbContext>(o => o.UseSqlite(Connection));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<ArchivalTestDbContext>());
        Services = services.BuildServiceProvider();

        using var scope = Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<ArchivalTestDbContext>();
        ctx.Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync()
    {
        await Connection.DisposeAsync();
        if (Services is IDisposable d)
        {
            d.Dispose();
        }
    }
}

public sealed class OutboxArchiveRow
{
    public Guid Id { get; set; }
    public string EventType { get; set; } = default!;
    public string Payload { get; set; } = default!;
    public DateTime OccurredOnUtc { get; set; }
    public DateTime ArchivedOnUtc { get; set; }
}

public sealed class ArchivalTestDbContext(DbContextOptions<ArchivalTestDbContext> options) : DbContext(options)
{
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();
    public DbSet<OutboxArchiveRow> Archive => Set<OutboxArchiveRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new OutboxMessageEntityTypeConfiguration("OrionGuard_Outbox"));
        modelBuilder.Entity<OutboxArchiveRow>(b =>
        {
            b.ToTable("OrionGuard_Outbox_Archive");
            b.HasKey(x => x.Id);
        });
    }
}

public class OutboxArchivalHostedServiceTests : IAsyncLifetime
{
    private ArchivalTestFixture fixture = default!;

    public Task InitializeAsync()
    {
        fixture = new ArchivalTestFixture();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await fixture.DisposeAsync();

    private OutboxArchivalHostedService NewSvc(OutboxArchivalOptions opts) =>
        new(opts,
            fixture.Services.GetRequiredService<IServiceScopeFactory>(),
            new NullDistributedLock(),
            NullLogger<OutboxArchivalHostedService>.Instance);

    private async Task SeedAsync(params OutboxMessage[] rows)
    {
        using var scope = fixture.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<ArchivalTestDbContext>();
        ctx.Outbox.AddRange(rows);
        await ctx.SaveChangesAsync();
    }

    private async Task<int> CountAsync()
    {
        using var scope = fixture.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<ArchivalTestDbContext>();
        return await ctx.Outbox.CountAsync();
    }

    private static OutboxMessage Row(DateTime? processedOnUtc, string? error = null) => new()
    {
        EventType = "test",
        Payload = "{}",
        OccurredOnUtc = processedOnUtc ?? DateTime.UtcNow,
        ProcessedOnUtc = processedOnUtc,
        Error = error,
    };

    [Fact]
    public async Task ArchiveBatchAsync_ShouldDeleteRowsOlderThanRetention()
    {
        await SeedAsync(
            Row(DateTime.UtcNow.AddDays(-45)),
            Row(DateTime.UtcNow.AddDays(-5)),
            Row(processedOnUtc: null));

        var svc = NewSvc(new OutboxArchivalOptions
        {
            RetentionPeriod = TimeSpan.FromDays(30),
            BatchSize = 10,
            PreserveDeadLetters = true,
        });

        var deleted = await svc.ArchiveBatchAsync(CancellationToken.None);

        Assert.Equal(1, deleted);
        Assert.Equal(2, await CountAsync());
    }

    [Fact]
    public async Task ArchiveBatchAsync_ShouldPreserveDeadLetters_WhenPreserveDeadLettersIsTrue()
    {
        await SeedAsync(
            Row(DateTime.UtcNow.AddDays(-45)),
            Row(DateTime.UtcNow.AddDays(-45), error: "boom"));

        var svc = NewSvc(new OutboxArchivalOptions { PreserveDeadLetters = true });

        var deleted = await svc.ArchiveBatchAsync(CancellationToken.None);

        Assert.Equal(1, deleted);
        Assert.Equal(1, await CountAsync());
    }

    [Fact]
    public async Task ArchiveBatchAsync_ShouldDeleteDeadLetters_WhenPreserveDeadLettersIsFalse()
    {
        await SeedAsync(
            Row(DateTime.UtcNow.AddDays(-45)),
            Row(DateTime.UtcNow.AddDays(-45), error: "boom"));

        var svc = NewSvc(new OutboxArchivalOptions { PreserveDeadLetters = false });

        var deleted = await svc.ArchiveBatchAsync(CancellationToken.None);

        Assert.Equal(2, deleted);
        Assert.Equal(0, await CountAsync());
    }

    [Fact]
    public void OutboxArchivalOptions_LockLeaseDuration_ShouldDefaultToFiveMinutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), new OutboxArchivalOptions().LockLeaseDuration);
    }

    [Fact]
    public async Task ArchiveBatchAsync_ShouldRespectBatchSize()
    {
        var rows = Enumerable.Range(0, 20)
            .Select(i => Row(DateTime.UtcNow.AddDays(-45).AddSeconds(-i)))
            .ToArray();
        await SeedAsync(rows);

        var svc = NewSvc(new OutboxArchivalOptions { BatchSize = 5 });

        var deleted = await svc.ArchiveBatchAsync(CancellationToken.None);

        Assert.Equal(5, deleted);
        Assert.Equal(15, await CountAsync());
    }

    [Fact]
    public async Task CopyToTableOutboxArchiver_ShouldCopyRowsToArchiveTableThenDelete()
    {
        await SeedAsync(
            Row(DateTime.UtcNow.AddDays(-45)),
            Row(DateTime.UtcNow.AddDays(-40)),
            Row(DateTime.UtcNow.AddDays(-1))); // not yet past retention

        var archiver = new CopyToTableOutboxArchiver<OutboxArchiveRow>(m => new OutboxArchiveRow
        {
            Id = m.Id,
            EventType = m.EventType,
            Payload = m.Payload,
            OccurredOnUtc = m.OccurredOnUtc,
            ArchivedOnUtc = DateTime.UtcNow,
        });

        var svc = new OutboxArchivalHostedService(
            new OutboxArchivalOptions { RetentionPeriod = TimeSpan.FromDays(30) },
            fixture.Services.GetRequiredService<IServiceScopeFactory>(),
            new NullDistributedLock(),
            NullLogger<OutboxArchivalHostedService>.Instance,
            archiver);

        var archived = await svc.ArchiveBatchAsync(CancellationToken.None);

        Assert.Equal(2, archived);
        Assert.Equal(1, await CountAsync());

        using var scope = fixture.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<ArchivalTestDbContext>();
        Assert.Equal(2, await ctx.Archive.CountAsync());
    }

    [Fact]
    public async Task IOutboxArchiver_Custom_ShouldBeInvokedInsteadOfDefault()
    {
        // Smoke-test that the strategy hook is honoured: a no-op archiver should leave
        // the table untouched even though the default would delete.
        await SeedAsync(Row(DateTime.UtcNow.AddDays(-45)));

        var noop = new NoopArchiver();
        var svc = new OutboxArchivalHostedService(
            new OutboxArchivalOptions(),
            fixture.Services.GetRequiredService<IServiceScopeFactory>(),
            new NullDistributedLock(),
            NullLogger<OutboxArchivalHostedService>.Instance,
            noop);

        var archived = await svc.ArchiveBatchAsync(CancellationToken.None);

        Assert.Equal(0, archived);
        Assert.Equal(1, noop.Invocations);
        Assert.Equal(1, await CountAsync());
    }

    private sealed class NoopArchiver : IOutboxArchiver
    {
        public int Invocations { get; private set; }
        public Task<int> ArchiveAsync(DbContext _, DateTime __, OutboxArchivalOptions ___, CancellationToken ____)
        {
            Invocations++;
            return Task.FromResult(0);
        }
    }
}
