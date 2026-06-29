using System.Collections.ObjectModel;

namespace Moongazing.OrionGuard.Migration;

/// <summary>
/// The outcome of migrating a single source file: the original and rewritten text plus every
/// construct that could not be migrated automatically.
/// </summary>
public sealed class FileMigrationResult
{
    /// <summary>Initializes a new <see cref="FileMigrationResult"/>.</summary>
    /// <param name="filePath">Absolute path of the source file.</param>
    /// <param name="originalText">The file's text before migration.</param>
    /// <param name="migratedText">The file's text after migration.</param>
    /// <param name="findings">Constructs that were left untouched and reported.</param>
    public FileMigrationResult(
        string filePath,
        string originalText,
        string migratedText,
        IReadOnlyList<MigrationFinding> findings)
    {
        FilePath = filePath;
        OriginalText = originalText;
        MigratedText = migratedText;
        Findings = new ReadOnlyCollection<MigrationFinding>(findings.ToList());
    }

    /// <summary>Absolute path of the source file.</summary>
    public string FilePath { get; }

    /// <summary>The file's text before migration.</summary>
    public string OriginalText { get; }

    /// <summary>The file's text after migration.</summary>
    public string MigratedText { get; }

    /// <summary>Constructs that could not be migrated automatically and were reported.</summary>
    public ReadOnlyCollection<MigrationFinding> Findings { get; }

    /// <summary>True when migration produced a textual change to the file.</summary>
    public bool HasChanges => !string.Equals(OriginalText, MigratedText, StringComparison.Ordinal);

    /// <summary>True when at least one construct needs manual follow-up.</summary>
    public bool HasUnmigrated => Findings.Count > 0;
}
