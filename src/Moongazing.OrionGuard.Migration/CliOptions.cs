namespace Moongazing.OrionGuard.Migration;

/// <summary>
/// The selected mode for a migrate run. The two modes are mutually exclusive and one must be
/// chosen explicitly so a user never writes files by accident.
/// </summary>
public enum MigrationMode
{
    /// <summary>Print the proposed diff and report without writing any file (<c>--report</c>).</summary>
    Report,

    /// <summary>Write the migrated files in place (<c>--apply</c>).</summary>
    Apply,
}

/// <summary>
/// Parsed options for the <c>migrate</c> command. Produced by <see cref="CliParser"/>.
/// </summary>
public sealed class CliOptions
{
    /// <summary>Initializes a new <see cref="CliOptions"/>.</summary>
    public CliOptions(string path, MigrationMode mode, string includeGlob)
    {
        Path = path;
        Mode = mode;
        IncludeGlob = includeGlob;
    }

    /// <summary>The directory or .cs file path to migrate.</summary>
    public string Path { get; }

    /// <summary>Whether to report (dry run) or apply changes.</summary>
    public MigrationMode Mode { get; }

    /// <summary>
    /// A simple include glob applied to file names when <see cref="Path"/> is a directory.
    /// Defaults to <c>*.cs</c>. Supports <c>*</c> and <c>?</c> wildcards.
    /// </summary>
    public string IncludeGlob { get; }
}
