using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Archival;

namespace Moongazing.OrionGuard.EntityFrameworkCore.Tests.Outbox.Archival;

public class CopyToTableOutboxArchiverTests
{
    [Fact]
    public async Task ArchiveAsync_WithARetryingExecutionStrategy_CopiesAndDeletesTheRows()
    {
        // A retrying strategy (what EnableRetryOnFailure installs) rejects a transaction the caller opens itself
        // unless the unit of work runs through the strategy.
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<ArchivalTestDbContext>(o => o.UseSqlite(connection, sqlite => sqlite.ExecutionStrategy(d => new RetryingStrategy(d))));
        await using var serviceProvider = services.BuildServiceProvider();
        await using var scope = serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ArchivalTestDbContext>();
        await db.Database.EnsureCreatedAsync();
        db.Outbox.AddRange(
            new OutboxMessage { EventType = "test", Payload = "{}", OccurredOnUtc = DateTime.UtcNow.AddDays(-45), ProcessedOnUtc = DateTime.UtcNow.AddDays(-45) },
            new OutboxMessage { EventType = "test", Payload = "{}", OccurredOnUtc = DateTime.UtcNow.AddDays(-40), ProcessedOnUtc = DateTime.UtcNow.AddDays(-40) });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var archiver = new CopyToTableOutboxArchiver<OutboxArchiveRow>(m => new OutboxArchiveRow
        {
            Id = m.Id,
            EventType = m.EventType,
            Payload = m.Payload,
            OccurredOnUtc = m.OccurredOnUtc,
            ArchivedOnUtc = DateTime.UtcNow,
        });

        var archived = await archiver.ArchiveAsync(db, DateTime.UtcNow.AddDays(-30), new OutboxArchivalOptions(), CancellationToken.None);

        Assert.Equal(2, archived);
        Assert.Equal(0, await db.Outbox.CountAsync());
        Assert.Equal(2, await db.Archive.CountAsync());
        Assert.Empty(db.ChangeTracker.Entries<OutboxArchiveRow>());   // copies are not left tracked as Added
    }

    private sealed class RetryingStrategy : ExecutionStrategy
    {
        public RetryingStrategy(ExecutionStrategyDependencies dependencies)
            : base(dependencies, maxRetryCount: 3, maxRetryDelay: TimeSpan.FromMilliseconds(10))
        {
        }

        protected override bool ShouldRetryOn(Exception exception) => false;
    }
}
