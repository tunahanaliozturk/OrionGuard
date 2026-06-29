using System.Text;

namespace Moongazing.OrionGuard.Migration;

/// <summary>
/// A file's text together with the encoding it was stored in, so a migrated file can be written
/// back in its original encoding (notably preserving or omitting a UTF-8 byte-order mark) instead
/// of silently switching encoding on apply.
/// </summary>
public sealed class FileContent
{
    /// <summary>Initializes a new <see cref="FileContent"/>.</summary>
    public FileContent(string text, Encoding encoding)
    {
        Text = text ?? throw new ArgumentNullException(nameof(text));
        Encoding = encoding ?? throw new ArgumentNullException(nameof(encoding));
    }

    /// <summary>The decoded file text.</summary>
    public string Text { get; }

    /// <summary>The encoding the file was read in, including any BOM preference.</summary>
    public Encoding Encoding { get; }
}

/// <summary>
/// The file-system seam the migration runner depends on. Abstracting it keeps the runner free of
/// real disk access in tests and makes the apply-versus-report distinction directly assertable.
/// </summary>
public interface IFileSystem
{
    /// <summary>True when the path is an existing directory.</summary>
    bool DirectoryExists(string path);

    /// <summary>True when the path is an existing file.</summary>
    bool FileExists(string path);

    /// <summary>
    /// Enumerates files under <paramref name="directory"/> recursively whose file name matches
    /// <paramref name="searchPattern"/> (a <c>*</c>/<c>?</c> glob). Build output directories
    /// (<c>bin</c>, <c>obj</c>) are skipped, and any subdirectory that cannot be accessed is skipped
    /// rather than aborting the whole enumeration.
    /// </summary>
    IEnumerable<string> EnumerateFiles(string directory, string searchPattern);

    /// <summary>Reads a file's text and the encoding it was stored in.</summary>
    FileContent ReadFile(string path);

    /// <summary>
    /// Writes <paramref name="contents"/> to <paramref name="path"/> in <paramref name="encoding"/>,
    /// atomically: the bytes are written to a temporary file in the same directory and then moved
    /// over the target, so a failure mid-write cannot leave a half-written or corrupt source file.
    /// </summary>
    void WriteFile(string path, string contents, Encoding encoding);
}

/// <summary>The production <see cref="IFileSystem"/> backed by <see cref="System.IO"/>.</summary>
public sealed class PhysicalFileSystem : IFileSystem
{
    // Output directories that must never be scanned: migrating generated/build artifacts is never
    // wanted and would also re-process copies of source under bin/.
    private static readonly string[] ExcludedDirectoryNames = { "bin", "obj" };

    /// <inheritdoc />
    public bool DirectoryExists(string path) => Directory.Exists(path);

    /// <inheritdoc />
    public bool FileExists(string path) => File.Exists(path);

    /// <inheritdoc />
    public IEnumerable<string> EnumerateFiles(string directory, string searchPattern)
    {
        // A manual recursive walk (rather than SearchOption.AllDirectories) is used so a single
        // inaccessible subdirectory is skipped gracefully instead of throwing and aborting the run,
        // and so bin/ and obj/ output trees can be pruned. Directory boundaries are respected: only
        // file names are matched against the glob, never path segments.
        var pending = new Stack<string>();
        pending.Push(directory);

        while (pending.Count > 0)
        {
            var current = pending.Pop();

            string[] files;
            try
            {
                files = Directory.GetFiles(current, searchPattern);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (DirectoryNotFoundException)
            {
                continue;
            }

            foreach (var file in files)
            {
                yield return file;
            }

            string[] subdirectories;
            try
            {
                subdirectories = Directory.GetDirectories(current);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (DirectoryNotFoundException)
            {
                continue;
            }

            foreach (var subdirectory in subdirectories)
            {
                var name = Path.GetFileName(subdirectory);
                if (IsExcludedDirectory(name))
                {
                    continue;
                }

                pending.Push(subdirectory);
            }
        }
    }

    /// <inheritdoc />
    public FileContent ReadFile(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var encoding = DetectEncoding(bytes, out var preambleLength);
        var text = encoding.GetString(bytes, preambleLength, bytes.Length - preambleLength);
        return new FileContent(text, encoding);
    }

    /// <inheritdoc />
    public void WriteFile(string path, string contents, Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(encoding);

        var directory = Path.GetDirectoryName(path);
        var temporaryPath = string.IsNullOrEmpty(directory)
            ? path + ".orionguard.tmp"
            : Path.Combine(directory, "." + Path.GetFileName(path) + ".orionguard.tmp");

        try
        {
            File.WriteAllText(temporaryPath, contents, encoding);

            // Move atomically over the target. File.Move with overwrite replaces the destination in
            // a single operation, so a reader never observes a partially written file.
            File.Move(temporaryPath, path, overwrite: true);
        }
        catch
        {
            // Best-effort cleanup so a failed write does not leave the temp file behind. The original
            // target file is untouched because it was never the write destination.
            TryDelete(temporaryPath);
            throw;
        }
    }

    private static bool IsExcludedDirectory(string name)
    {
        foreach (var excluded in ExcludedDirectoryNames)
        {
            if (string.Equals(name, excluded, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Detects the encoding of a file's raw bytes from its byte-order mark. A UTF-8 BOM, UTF-16
    /// LE/BE, or UTF-32 BOM is honoured; with no BOM the file is treated as UTF-8 without a BOM
    /// (the .NET default), so a BOM-less file stays BOM-less on write.
    /// </summary>
    private static Encoding DetectEncoding(byte[] bytes, out int preambleLength)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            preambleLength = 3;
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        }

        if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0x00 && bytes[3] == 0x00)
        {
            preambleLength = 4;
            return new UTF32Encoding(bigEndian: false, byteOrderMark: true);
        }

        if (bytes.Length >= 4 && bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0xFE && bytes[3] == 0xFF)
        {
            preambleLength = 4;
            return new UTF32Encoding(bigEndian: true, byteOrderMark: true);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            preambleLength = 2;
            return new UnicodeEncoding(bigEndian: false, byteOrderMark: true);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            preambleLength = 2;
            return new UnicodeEncoding(bigEndian: true, byteOrderMark: true);
        }

        preambleLength = 0;
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Cleanup is best effort; surface the original failure instead.
        }
        catch (UnauthorizedAccessException)
        {
            // Cleanup is best effort; surface the original failure instead.
        }
    }
}
