using System.Text;
using Moongazing.OrionGuard.Migration;

namespace Moongazing.OrionGuard.Migration.Tests;

/// <summary>
/// A test double for <see cref="IFileSystem"/> that holds files in a dictionary so the runner's
/// read/write behaviour can be asserted without touching disk. Tracks the encoding each file was
/// stored in and can be told to fail a write for a specific path to exercise I/O-failure handling.
/// </summary>
public sealed class InMemoryFileSystem : IFileSystem
{
    private readonly Dictionary<string, FileContent> _files = new(StringComparer.Ordinal);
    private readonly HashSet<string> _directories = new(StringComparer.Ordinal);
    private readonly HashSet<string> _writeFailures = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, FileContent> Files => _files;

    public int WriteCount { get; private set; }

    public void AddFile(string path, string contents) =>
        AddFile(path, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    public void AddFile(string path, string contents, Encoding encoding) =>
        _files[path] = new FileContent(contents, encoding);

    public void AddDirectory(string path) => _directories.Add(path);

    /// <summary>Marks a path so the next <see cref="WriteFile"/> to it throws, as a real I/O fault would.</summary>
    public void FailWriteFor(string path) => _writeFailures.Add(path);

    public bool DirectoryExists(string path) => _directories.Contains(path);

    public bool FileExists(string path) => _files.ContainsKey(path);

    public IEnumerable<string> EnumerateFiles(string directory, string searchPattern)
    {
        var suffix = searchPattern.StartsWith('*') ? searchPattern[1..] : searchPattern;

        foreach (var path in _files.Keys)
        {
            if (path.StartsWith(directory, StringComparison.Ordinal) &&
                path.EndsWith(suffix, StringComparison.Ordinal))
            {
                yield return path;
            }
        }
    }

    public FileContent ReadFile(string path) => _files[path];

    public void WriteFile(string path, string contents, Encoding encoding)
    {
        if (_writeFailures.Contains(path))
        {
            // Simulate a per-file I/O failure. An atomic writer never touches the original file on
            // failure, so leave the stored content unchanged -- exactly what the runner relies on.
            throw new IOException($"simulated write failure for {path}");
        }

        _files[path] = new FileContent(contents, encoding);
        WriteCount++;
    }
}
