using System.Text;

namespace Moongazing.OrionGuard.Migration;

/// <summary>
/// The exit code categories the tool reports to the shell.
/// </summary>
public enum MigrationExitCode
{
    /// <summary>Everything migrated cleanly; nothing needs manual follow-up.</summary>
    Success = 0,

    /// <summary>Migration ran but at least one construct needs manual follow-up.</summary>
    ManualFollowUpRequired = 1,

    /// <summary>The arguments or input path were invalid; nothing was processed.</summary>
    UsageError = 2,
}

/// <summary>
/// Orchestrates a migrate run end to end: resolve the input path to a set of files, migrate each,
/// emit the diff/report (or write files when applying), and compute the process exit code. All
/// output goes through an injected <see cref="TextWriter"/> and all disk access through an injected
/// <see cref="IFileSystem"/>, so the whole run is observable in tests.
/// </summary>
public sealed class MigrationRunner
{
    private readonly IFileSystem _fileSystem;
    private readonly TextWriter _output;

    /// <summary>Initializes a new <see cref="MigrationRunner"/>.</summary>
    public MigrationRunner(IFileSystem fileSystem, TextWriter output)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    /// <summary>Runs migration for the given options and returns the exit code.</summary>
    public MigrationExitCode Run(CliOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!TryResolveFiles(options, out var files, out var resolveError))
        {
            _output.WriteLine(resolveError);
            return MigrationExitCode.UsageError;
        }

        var apply = options.Mode == MigrationMode.Apply;
        var changedFiles = 0;
        var allFindings = new List<MigrationFinding>();

        foreach (var file in files)
        {
            var original = _fileSystem.ReadAllText(file);
            var result = MigrationEngine.Migrate(file, original);

            allFindings.AddRange(result.Findings);

            if (!result.HasChanges && !result.HasUnmigrated)
            {
                continue;
            }

            if (result.HasChanges)
            {
                changedFiles++;

                if (apply)
                {
                    _fileSystem.WriteAllText(file, result.MigratedText);
                    _output.WriteLine($"migrated: {file}");
                }
                else
                {
                    _output.WriteLine(UnifiedDiff.Render(file, result.OriginalText, result.MigratedText));
                }
            }
            else if (!apply)
            {
                // No textual change but there are findings (every chain was unsupported): still
                // surface the file in report mode so the user knows it was inspected.
                _output.WriteLine($"no automatic changes: {file}");
            }
        }

        WriteSummary(apply, files.Count, changedFiles, allFindings);

        return allFindings.Count > 0
            ? MigrationExitCode.ManualFollowUpRequired
            : MigrationExitCode.Success;
    }

    private bool TryResolveFiles(
        CliOptions options, out IReadOnlyList<string> files, out string error)
    {
        if (_fileSystem.FileExists(options.Path))
        {
            files = new[] { options.Path };
            error = string.Empty;
            return true;
        }

        if (_fileSystem.DirectoryExists(options.Path))
        {
            files = _fileSystem
                .EnumerateFiles(options.Path, options.IncludeGlob)
                .OrderBy(static p => p, StringComparer.Ordinal)
                .ToList();
            error = string.Empty;
            return true;
        }

        files = Array.Empty<string>();
        error = $"Path not found: {options.Path}";
        return false;
    }

    private void WriteSummary(
        bool apply, int fileCount, int changedFiles, List<MigrationFinding> findings)
    {
        var summary = new StringBuilder();
        summary.Append('\n');
        summary.Append("OrionGuard migration summary").Append('\n');
        summary.Append("============================").Append('\n');
        summary.Append("Mode:           ").Append(apply ? "apply" : "report").Append('\n');
        summary.Append("Files scanned:  ").Append(fileCount).Append('\n');
        summary.Append(apply ? "Files written:  " : "Files to change: ")
            .Append(changedFiles).Append('\n');
        summary.Append("Manual follow-ups: ").Append(findings.Count).Append('\n');

        if (findings.Count > 0)
        {
            summary.Append('\n');
            summary.Append("The following constructs were left untouched (see TODO markers):").Append('\n');
            foreach (var finding in findings)
            {
                summary
                    .Append("  ")
                    .Append(finding.FilePath)
                    .Append(':')
                    .Append(finding.Line)
                    .Append("  ")
                    .Append(finding.Rule)
                    .Append(" - ")
                    .Append(finding.Reason)
                    .Append('\n');
            }
        }

        _output.Write(summary.ToString());
        _output.Flush();
    }
}
