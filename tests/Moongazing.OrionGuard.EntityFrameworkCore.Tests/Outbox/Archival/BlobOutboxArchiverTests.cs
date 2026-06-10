namespace Moongazing.OrionGuard.EntityFrameworkCore.Tests.Outbox.Archival;

using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Archival;
using Xunit;

public sealed class BlobOutboxArchiverTests
{
    private sealed class CapturingSink : IOutboxArchiveSink
    {
        public List<(string KeyHint, byte[] Bytes)> Writes { get; } = new();
        public bool ShouldThrow { get; set; }
        public Task WriteAsync(string keyHint, ReadOnlyMemory<byte> payload, CancellationToken ct)
        {
            if (ShouldThrow)
            {
                throw new InvalidOperationException("sink boom");
            }
            Writes.Add((keyHint, payload.ToArray()));
            return Task.CompletedTask;
        }
    }

    private sealed class ArchivalDbContext : DbContext
    {
        public ArchivalDbContext(DbContextOptions<ArchivalDbContext> options) : base(options) { }
        public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<OutboxMessage>(b =>
            {
                b.ToTable("Outbox");
                b.HasKey(m => m.Id);
                b.Property(m => m.EventType).IsRequired();
                b.Property(m => m.Payload).IsRequired();
            });
        }
    }

    private static ArchivalDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ArchivalDbContext>()
            .UseSqlite($"DataSource=blob-archiver-{Guid.NewGuid():N};Mode=Memory;Cache=Shared")
            .Options;
        var ctx = new ArchivalDbContext(options);
        ctx.Database.OpenConnection();
        ctx.Database.EnsureCreated();
        return ctx;
    }

    private static OutboxMessage Row(DateTime processed, DateTime occurred, string? error = null) => new()
    {
        Id = Guid.NewGuid(),
        EventType = "Demo.Event",
        Payload = "{\"a\":1}",
        OccurredOnUtc = occurred,
        ProcessedOnUtc = processed,
        RetryCount = error is null ? 0 : 5,
        Error = error,
        CorrelationId = "corr-1",
    };

    [Fact]
    public async Task ArchiveAsync_writes_jsonl_payload_to_sink_then_deletes_eligible_rows()
    {
        using var db = NewContext();
        var anchor = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        db.Outbox.AddRange(
            Row(processed: anchor, occurred: anchor.AddMinutes(-10)),
            Row(processed: anchor.AddDays(1), occurred: anchor));
        await db.SaveChangesAsync();

        var sink = new CapturingSink();
        var fixedNow = new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc);
        var sut = new BlobOutboxArchiver(sink, () => fixedNow);

        var cutoff = anchor.AddDays(10);
        var deleted = await sut.ArchiveAsync(db, cutoff,
            new OutboxArchivalOptions { BatchSize = 100, PreserveDeadLetters = false },
            CancellationToken.None);

        Assert.Equal(2, deleted);
        var write = Assert.Single(sink.Writes);
        Assert.Equal("outbox-2026-07-01T12-00-00Z", write.KeyHint);
        var lines = Encoding.UTF8.GetString(write.Bytes)
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        foreach (var line in lines)
        {
            using var doc = JsonDocument.Parse(line);
            Assert.True(doc.RootElement.TryGetProperty("id", out _));
            Assert.True(doc.RootElement.TryGetProperty("eventType", out _));
        }
        Assert.Equal(0, await db.Outbox.CountAsync());
    }

    [Fact]
    public async Task ArchiveAsync_does_not_delete_when_sink_throws()
    {
        using var db = NewContext();
        var anchor = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        db.Outbox.Add(Row(processed: anchor, occurred: anchor));
        await db.SaveChangesAsync();

        var sink = new CapturingSink { ShouldThrow = true };
        var sut = new BlobOutboxArchiver(sink);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.ArchiveAsync(db, anchor.AddDays(10),
                new OutboxArchivalOptions { BatchSize = 100, PreserveDeadLetters = false },
                CancellationToken.None));

        Assert.Equal(1, await db.Outbox.CountAsync());
    }

    [Fact]
    public async Task ArchiveAsync_preserves_dead_letters_when_option_is_set()
    {
        using var db = NewContext();
        var anchor = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        db.Outbox.AddRange(
            Row(processed: anchor, occurred: anchor),
            Row(processed: anchor, occurred: anchor, error: "boom"));
        await db.SaveChangesAsync();

        var sink = new CapturingSink();
        var sut = new BlobOutboxArchiver(sink);

        var deleted = await sut.ArchiveAsync(db, anchor.AddDays(10),
            new OutboxArchivalOptions { BatchSize = 100, PreserveDeadLetters = true },
            CancellationToken.None);

        Assert.Equal(1, deleted);
        var remaining = await db.Outbox.SingleAsync();
        Assert.Equal("boom", remaining.Error);
    }

    [Fact]
    public async Task ArchiveAsync_no_op_when_no_eligible_rows()
    {
        using var db = NewContext();
        db.Outbox.Add(Row(processed: DateTime.UtcNow, occurred: DateTime.UtcNow));
        await db.SaveChangesAsync();

        var sink = new CapturingSink();
        var sut = new BlobOutboxArchiver(sink);

        // Cutoff is BEFORE the row's processed time so nothing is eligible.
        var deleted = await sut.ArchiveAsync(db, DateTime.UtcNow.AddYears(-10),
            new OutboxArchivalOptions { BatchSize = 100 },
            CancellationToken.None);

        Assert.Equal(0, deleted);
        Assert.Empty(sink.Writes);
        Assert.Equal(1, await db.Outbox.CountAsync());
    }

    [Fact]
    public void Constructor_rejects_null_sink()
    {
        Assert.Throws<ArgumentNullException>(() => new BlobOutboxArchiver(null!));
    }

    [Fact]
    public async Task LocalFileOutboxArchiveSink_writes_file_with_jsonl_extension()
    {
        var temp = Path.Combine(Path.GetTempPath(), $"orionguard-blob-test-{Guid.NewGuid():N}");
        try
        {
            var sink = new LocalFileOutboxArchiveSink(temp);
            await sink.WriteAsync("outbox-batch-1", Encoding.UTF8.GetBytes("hello"), CancellationToken.None);

            var path = Path.Combine(temp, "outbox-batch-1.jsonl");
            Assert.True(File.Exists(path));
            Assert.Equal("hello", await File.ReadAllTextAsync(path));
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, recursive: true);
            }
        }
    }
}
