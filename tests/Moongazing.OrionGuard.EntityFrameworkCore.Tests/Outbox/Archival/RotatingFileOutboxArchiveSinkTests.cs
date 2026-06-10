namespace Moongazing.OrionGuard.EntityFrameworkCore.Tests.Outbox.Archival;

using System.Text;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Archival;
using Xunit;

public sealed class RotatingFileOutboxArchiveSinkTests : IDisposable
{
    private readonly string root;

    public RotatingFileOutboxArchiveSinkTests()
    {
        root = Path.Combine(Path.GetTempPath(), $"orionguard-rotating-{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private RotatingFileOutboxArchiveSink NewSink(
        DateTime? fixedNow = null,
        long maxFileBytes = 64 * 1024 * 1024,
        int maxShards = 9999)
    {
        var opts = new RotatingFileOutboxArchiveSinkOptions
        {
            RootDirectory = root,
            MaxFileBytes = maxFileBytes,
            MaxShardsPerDay = maxShards,
        };
        return new RotatingFileOutboxArchiveSink(opts, () => fixedNow ?? new DateTime(2026, 6, 11, 12, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task Writes_to_day_partitioned_directory_with_shard_suffix()
    {
        var sink = NewSink(fixedNow: new DateTime(2026, 6, 11, 12, 0, 0, DateTimeKind.Utc));

        await sink.WriteAsync("outbox-batch-1", Encoding.UTF8.GetBytes("hello"), CancellationToken.None);

        var expected = Path.Combine(root, "2026-06-11", "outbox-0000.jsonl");
        Assert.True(File.Exists(expected));
        Assert.Equal("hello", await File.ReadAllTextAsync(expected));
    }

    [Fact]
    public async Task Appends_to_existing_shard_when_fits_under_MaxFileBytes()
    {
        var sink = NewSink(maxFileBytes: 100);

        await sink.WriteAsync("batch", Encoding.UTF8.GetBytes("aaaa"), CancellationToken.None);
        await sink.WriteAsync("batch", Encoding.UTF8.GetBytes("bbbb"), CancellationToken.None);

        var path = Path.Combine(root, "2026-06-11", "outbox-0000.jsonl");
        Assert.Equal("aaaabbbb", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task Rolls_to_next_shard_when_existing_shard_would_overflow()
    {
        var sink = NewSink(maxFileBytes: 5);

        await sink.WriteAsync("batch", Encoding.UTF8.GetBytes("AAAAA"), CancellationToken.None);
        // Second write would overflow shard 0 (5+5=10 > 5) so it goes to shard 1.
        await sink.WriteAsync("batch", Encoding.UTF8.GetBytes("BBBBB"), CancellationToken.None);

        var dayDir = Path.Combine(root, "2026-06-11");
        Assert.Equal("AAAAA", await File.ReadAllTextAsync(Path.Combine(dayDir, "outbox-0000.jsonl")));
        Assert.Equal("BBBBB", await File.ReadAllTextAsync(Path.Combine(dayDir, "outbox-0001.jsonl")));
    }

    [Fact]
    public async Task Throws_when_all_shards_full()
    {
        var sink = NewSink(maxFileBytes: 1, maxShards: 2);

        await sink.WriteAsync("batch", new byte[] { 1 }, CancellationToken.None);
        await sink.WriteAsync("batch", new byte[] { 2 }, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sink.WriteAsync("batch", new byte[] { 3 }, CancellationToken.None));
    }

    [Fact]
    public async Task Different_days_get_separate_subdirectories()
    {
        var sink1 = NewSink(fixedNow: new DateTime(2026, 6, 11, 23, 59, 59, DateTimeKind.Utc));
        await sink1.WriteAsync("batch", Encoding.UTF8.GetBytes("day1"), CancellationToken.None);

        var sink2 = NewSink(fixedNow: new DateTime(2026, 6, 12, 0, 0, 1, DateTimeKind.Utc));
        await sink2.WriteAsync("batch", Encoding.UTF8.GetBytes("day2"), CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(root, "2026-06-11", "outbox-0000.jsonl")));
        Assert.True(File.Exists(Path.Combine(root, "2026-06-12", "outbox-0000.jsonl")));
    }

    [Fact]
    public async Task Throws_when_single_payload_exceeds_MaxFileBytes()
    {
        var sink = NewSink(maxFileBytes: 4);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sink.WriteAsync("batch", new byte[] { 1, 2, 3, 4, 5 }, CancellationToken.None));
    }

    [Fact]
    public async Task Path_traversal_in_keyHint_does_not_escape_root_directory()
    {
        // Even if the caller passes a malicious keyHint, the sink uses a stable per-day
        // file naming scheme and never mixes the hint into the path.
        var sink = NewSink();

        await sink.WriteAsync("../../../etc/passwd", Encoding.UTF8.GetBytes("payload"), CancellationToken.None);

        var safePath = Path.Combine(root, "2026-06-11", "outbox-0000.jsonl");
        Assert.True(File.Exists(safePath));
        Assert.False(File.Exists("/etc/passwd"));
    }

    [Fact]
    public void Options_validate_at_construction()
    {
        Assert.Throws<ArgumentException>(() => new RotatingFileOutboxArchiveSink(
            new RotatingFileOutboxArchiveSinkOptions { RootDirectory = string.Empty }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RotatingFileOutboxArchiveSink(
            new RotatingFileOutboxArchiveSinkOptions { RootDirectory = root, MaxFileBytes = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RotatingFileOutboxArchiveSink(
            new RotatingFileOutboxArchiveSinkOptions { RootDirectory = root, MaxShardsPerDay = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RotatingFileOutboxArchiveSink(
            new RotatingFileOutboxArchiveSinkOptions { RootDirectory = root, MaxShardsPerDay = 10000 }));
    }
}
