using Moongazing.OrionGuard.Migration;

namespace Moongazing.OrionGuard.Migration.Tests;

/// <summary>
/// A test double for <see cref="IFileSystem"/> that holds files in a dictionary so the runner's
/// read/write behaviour can be asserted without touching disk.
/// </summary>
public sealed class InMemoryFileSystem : IFileSystem
{
    private readonly Dictionary<string, string> _files = new(StringComparer.Ordinal);
    private readonly HashSet<string> _directories = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, string> Files => _files;

    public int WriteCount { get; private set; }

    public void AddFile(string path, string contents) => _files[path] = contents;

    public void AddDirectory(string path) => _directories.Add(path);

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

    public string ReadAllText(string path) => _files[path];

    public void WriteAllText(string path, string contents)
    {
        _files[path] = contents;
        WriteCount++;
    }
}
