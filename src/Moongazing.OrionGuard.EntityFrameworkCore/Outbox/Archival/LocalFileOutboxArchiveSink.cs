namespace Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Archival;

using System.IO;

/// <summary>
/// Reference <see cref="IOutboxArchiveSink"/> that writes the archival payload to a local
/// directory. Useful for local development / testing and as a fallback when consumers do
/// not yet have an object-store credential pipeline. Production deployments register a
/// consumer-owned cloud-object-store sink instead (S3, Azure Blob, GCS).
/// </summary>
public sealed class LocalFileOutboxArchiveSink : IOutboxArchiveSink
{
    private readonly string root;

    /// <summary>Construct with the destination directory (created on first write if missing).</summary>
    public LocalFileOutboxArchiveSink(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(rootDirectory);
        root = rootDirectory;
    }

    /// <inheritdoc />
    public async Task WriteAsync(string keyHint, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(keyHint);
        Directory.CreateDirectory(root);
        // Append the .jsonl extension because BlobOutboxArchiver emits newline-delimited
        // JSON records by default. Consumers wiring a binary archive can subclass /
        // wrap this sink with a different extension.
        var path = Path.Combine(root, $"{keyHint}.jsonl");
        await using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            useAsync: true);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
