using System.Text;
using System.Text.RegularExpressions;
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

    /// <summary>
    /// Recursively enumerates files under <paramref name="directory"/> whose file NAME matches
    /// <paramref name="searchPattern"/>, modelling the production <see cref="PhysicalFileSystem"/>
    /// faithfully: directory boundaries are honoured (a sibling directory sharing a name prefix, such
    /// as <c>/repo2</c> when enumerating <c>/repo</c>, is never matched) and the full glob contract is
    /// applied to the file name -- <c>*</c>, <c>?</c>, and mid-pattern wildcards all behave as the
    /// real <c>Directory.GetFiles</c> glob does, rather than as a bare suffix test.
    /// </summary>
    public IEnumerable<string> EnumerateFiles(string directory, string searchPattern)
    {
        var prefix = NormalizeDirectory(directory);
        var nameMatcher = GlobToRegex(searchPattern);

        foreach (var path in _files.Keys)
        {
            if (!IsUnderDirectory(path, prefix))
            {
                continue;
            }

            var fileName = GetFileName(path);
            if (nameMatcher.IsMatch(fileName))
            {
                yield return path;
            }
        }
    }

    // Normalize separators and guarantee a single trailing separator so prefix comparison treats
    // `/repo` and `/repo/` alike and never matches a sibling such as `/repo2`.
    private static string NormalizeDirectory(string directory)
    {
        var unified = directory.Replace('\\', '/');
        return unified.EndsWith('/') ? unified : unified + "/";
    }

    private static bool IsUnderDirectory(string path, string normalizedPrefix) =>
        path.Replace('\\', '/').StartsWith(normalizedPrefix, StringComparison.Ordinal);

    private static string GetFileName(string path)
    {
        var unified = path.Replace('\\', '/');
        var lastSeparator = unified.LastIndexOf('/');
        return lastSeparator < 0 ? unified : unified[(lastSeparator + 1)..];
    }

    // Translate a `*`/`?` file-name glob into an anchored, case-insensitive regex (matching the
    // real EnumerateFiles, which delegates to the OS glob). `*` matches any run of characters, `?`
    // matches exactly one, and every other character is treated literally.
    private static Regex GlobToRegex(string searchPattern)
    {
        var builder = new StringBuilder("^");
        foreach (var ch in searchPattern)
        {
            builder.Append(ch switch
            {
                '*' => ".*",
                '?' => ".",
                _ => Regex.Escape(ch.ToString()),
            });
        }

        builder.Append('$');
        return new Regex(builder.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
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
