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

    // Translate a `*`/`?` file-name glob into an anchored regex matching the real EnumerateFiles,
    // which delegates to `Directory.GetFiles(dir, pattern)`. That legacy overload matches with
    // `MatchCasing.PlatformDefault`, whose case-sensitivity the BCL gleans from the host file system
    // (case-insensitive on Windows/NTFS, case-sensitive on a typical Linux file system). Hardcoding
    // `IgnoreCase` would diverge from the real file system on case-sensitive platforms, so the casing
    // is taken from the platform default probed in <see cref="PlatformCaseSensitivity"/>. `*` matches
    // any run of characters, `?` matches exactly one, and every other character is literal.
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

        var options = RegexOptions.CultureInvariant;
        if (!PlatformCaseSensitivity.IsCaseSensitive)
        {
            options |= RegexOptions.IgnoreCase;
        }

        return new Regex(builder.ToString(), options);
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

    /// <summary>
    /// The file-name glob case-sensitivity of the host platform, determined the same way the BCL
    /// resolves <see cref="System.IO.MatchCasing.PlatformDefault"/>: by observing the case
    /// sensitivity of the temporary folder. This keeps the in-memory glob aligned with what the real
    /// <see cref="PhysicalFileSystem"/> (which calls <c>Directory.GetFiles</c>) actually does on the
    /// machine running the tests, instead of assuming one fixed casing.
    /// </summary>
    internal static class PlatformCaseSensitivity
    {
        public static readonly bool IsCaseSensitive = Probe();

        private static bool Probe()
        {
            var directory = Path.Combine(Path.GetTempPath(), "orionguard-case-probe-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var lowerName = "probe-" + Guid.NewGuid().ToString("N") + ".cs";
                File.WriteAllText(Path.Combine(directory, lowerName), string.Empty);

                // Search with the upper-cased name. If the case-insensitive platform default finds the
                // lower-cased file, the file system is case-insensitive; if nothing matches, it is
                // case-sensitive.
                var matches = Directory.GetFiles(directory, lowerName.ToUpperInvariant());
                return matches.Length == 0;
            }
            finally
            {
                try
                {
                    Directory.Delete(directory, recursive: true);
                }
                catch (IOException)
                {
                    // Best-effort cleanup of the probe directory.
                }
                catch (UnauthorizedAccessException)
                {
                    // Best-effort cleanup of the probe directory.
                }
            }
        }
    }
}
