namespace Moongazing.OrionGuard.Migration;

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
    /// <paramref name="searchPattern"/> (a <c>*</c>/<c>?</c> glob).
    /// </summary>
    IEnumerable<string> EnumerateFiles(string directory, string searchPattern);

    /// <summary>Reads the entire contents of a file as text.</summary>
    string ReadAllText(string path);

    /// <summary>Writes text to a file, overwriting it.</summary>
    void WriteAllText(string path, string contents);
}

/// <summary>The production <see cref="IFileSystem"/> backed by <see cref="System.IO"/>.</summary>
public sealed class PhysicalFileSystem : IFileSystem
{
    /// <inheritdoc />
    public bool DirectoryExists(string path) => Directory.Exists(path);

    /// <inheritdoc />
    public bool FileExists(string path) => File.Exists(path);

    /// <inheritdoc />
    public IEnumerable<string> EnumerateFiles(string directory, string searchPattern) =>
        Directory.EnumerateFiles(directory, searchPattern, SearchOption.AllDirectories);

    /// <inheritdoc />
    public string ReadAllText(string path) => File.ReadAllText(path);

    /// <inheritdoc />
    public void WriteAllText(string path, string contents) => File.WriteAllText(path, contents);
}
