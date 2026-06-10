namespace Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Archival;

using System.Globalization;
using System.IO;

/// <summary>
/// Production-grade <see cref="IOutboxArchiveSink"/> that writes JSON Lines payloads to
/// rotating files under a root directory. Pairs with <see cref="BlobOutboxArchiver"/> for
/// deployments that ship archives to a local NFS / EFS mount + a downstream batch job
/// that uploads to cold storage. Rotation strategy:
/// <list type="bullet">
///   <item><description>One subdirectory per UTC day (<c>yyyy-MM-dd</c>) so the operator can prune by date.</description></item>
///   <item><description>Within a day, files are split when <see cref="RotatingFileOutboxArchiveSinkOptions.MaxFileBytes"/> is reached - the current shard's index increments to the next available number.</description></item>
///   <item><description>File names include the archive's key hint + a 4-digit shard suffix so two archivers writing to the same root cannot overwrite each other.</description></item>
/// </list>
/// </summary>
public sealed class RotatingFileOutboxArchiveSink : IOutboxArchiveSink
{
    private readonly RotatingFileOutboxArchiveSinkOptions options;
    private readonly Func<DateTime> nowUtc;

    /// <summary>Construct with sink options. The <c>nowUtc</c> hook lets tests pin the day directory.</summary>
    public RotatingFileOutboxArchiveSink(
        RotatingFileOutboxArchiveSinkOptions options,
        Func<DateTime>? nowUtc = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.ValidateAndNormalise();
        this.options = options;
        this.nowUtc = nowUtc ?? (() => DateTime.UtcNow);
    }

    /// <inheritdoc />
    public async Task WriteAsync(string keyHint, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(keyHint);
        // Reject payloads larger than MaxFileBytes at the door rather than silently
        // creating an over-cap file. The archiver should split the batch before invoking
        // the sink when batches grow this large.
        if (payload.Length > options.MaxFileBytes)
        {
            throw new InvalidOperationException(
                $"RotatingFileOutboxArchiveSink: payload size {payload.Length} bytes exceeds " +
                $"MaxFileBytes ({options.MaxFileBytes}). Reduce the archiver's batch size or raise the cap.");
        }

        var day = nowUtc().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var dayDir = Path.Combine(options.RootDirectory, day);
        Directory.CreateDirectory(dayDir);

        // Stable per-day filename prefix - the caller's keyHint is intentionally NOT
        // mixed into the path because (a) BlobOutboxArchiver already includes a
        // timestamp + guid in the hint, so layering both yields redundant unique names
        // that prevent batches from packing into the same shard; and (b) using untrusted
        // input as a path component is a path-traversal risk (`..` / `/` would escape
        // the root directory). The shard scheme alone manages rotation.
        for (var shard = 0; shard < options.MaxShardsPerDay; shard++)
        {
            var fileName = $"outbox-{shard.ToString("D4", CultureInfo.InvariantCulture)}.jsonl";
            var path = Path.Combine(dayDir, fileName);
            var existing = new FileInfo(path);
            if (!existing.Exists)
            {
                // Race-resilient create: another writer may create the same shard
                // between FileInfo.Exists and our open. CreateNew throws IOException in
                // that case; treat that as "shard now exists, try to append".
                try
                {
                    await WriteFileAsync(path, payload, cancellationToken, mode: FileMode.CreateNew).ConfigureAwait(false);
                    return;
                }
                catch (IOException) when (File.Exists(path))
                {
                    existing = new FileInfo(path);
                    if (existing.Length + payload.Length <= options.MaxFileBytes)
                    {
                        await WriteFileAsync(path, payload, cancellationToken, mode: FileMode.Append).ConfigureAwait(false);
                        return;
                    }
                    continue;
                }
            }
            if (existing.Length + payload.Length <= options.MaxFileBytes)
            {
                await WriteFileAsync(path, payload, cancellationToken, mode: FileMode.Append).ConfigureAwait(false);
                return;
            }
        }
        throw new InvalidOperationException(
            $"RotatingFileOutboxArchiveSink: no available shard under '{dayDir}'. " +
            $"MaxShardsPerDay ({options.MaxShardsPerDay}) reached and every shard is full. " +
            "Consider raising MaxShardsPerDay or MaxFileBytes.");
    }

    private static async Task WriteFileAsync(string path, ReadOnlyMemory<byte> payload, CancellationToken ct, FileMode mode)
    {
        await using var stream = new FileStream(
            path, mode, FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true);
        await stream.WriteAsync(payload, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }
}

/// <summary>Configuration for <see cref="RotatingFileOutboxArchiveSink"/>.</summary>
public sealed class RotatingFileOutboxArchiveSinkOptions
{
    /// <summary>Root directory under which day-partitioned subdirectories are created.</summary>
    public string RootDirectory { get; set; } = string.Empty;

    /// <summary>Maximum bytes per shard file before the sink rotates to the next shard. Default 64 MiB.</summary>
    public long MaxFileBytes { get; set; } = 64L * 1024L * 1024L;

    /// <summary>Upper bound on shards per UTC day. Default 9999 (matches 4-digit suffix range).</summary>
    public int MaxShardsPerDay { get; set; } = 9999;

    internal void ValidateAndNormalise()
    {
        if (string.IsNullOrWhiteSpace(RootDirectory))
        {
            throw new ArgumentException(
                "RotatingFileOutboxArchiveSinkOptions.RootDirectory must be non-empty.",
                nameof(RootDirectory));
        }
        if (MaxFileBytes < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxFileBytes), MaxFileBytes,
                "RotatingFileOutboxArchiveSinkOptions.MaxFileBytes must be at least 1 byte.");
        }
        if (MaxShardsPerDay is < 1 or > 9999)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxShardsPerDay), MaxShardsPerDay,
                "RotatingFileOutboxArchiveSinkOptions.MaxShardsPerDay must be in [1, 9999].");
        }
    }
}
