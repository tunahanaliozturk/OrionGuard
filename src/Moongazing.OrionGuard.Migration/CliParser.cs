namespace Moongazing.OrionGuard.Migration;

/// <summary>The outcome of parsing the command line.</summary>
public sealed class CliParseResult
{
    private CliParseResult(CliOptions? options, string? error, bool helpRequested)
    {
        Options = options;
        Error = error;
        HelpRequested = helpRequested;
    }

    /// <summary>The parsed options when parsing succeeded.</summary>
    public CliOptions? Options { get; }

    /// <summary>An error message when the arguments were invalid.</summary>
    public string? Error { get; }

    /// <summary>True when the user asked for help (<c>--help</c> / <c>-h</c>).</summary>
    public bool HelpRequested { get; }

    internal static CliParseResult Ok(CliOptions options) => new(options, null, false);

    internal static CliParseResult Fail(string error) => new(null, error, false);

    internal static CliParseResult Help() => new(null, null, true);
}

/// <summary>
/// A small hand-rolled parser for the <c>migrate</c> command. Kept dependency-free on purpose so
/// the tool's only third-party dependency is Roslyn.
/// </summary>
public static class CliParser
{
    /// <summary>
    /// Parses arguments of the form <c>migrate &lt;path&gt; [--report|--apply] [--include &lt;glob&gt;]</c>.
    /// </summary>
    public static CliParseResult Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Count == 0)
        {
            return CliParseResult.Help();
        }

        if (HasHelpFlag(args))
        {
            return CliParseResult.Help();
        }

        if (!string.Equals(args[0], "migrate", StringComparison.Ordinal))
        {
            return CliParseResult.Fail($"Unknown command '{args[0]}'. The only command is 'migrate'.");
        }

        string? path = null;
        MigrationMode? mode = null;
        var includeGlob = "*.cs";

        for (var i = 1; i < args.Count; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--report":
                case "--dry-run":
                    if (mode == MigrationMode.Apply)
                    {
                        return CliParseResult.Fail("Specify only one of --report or --apply.");
                    }

                    mode = MigrationMode.Report;
                    break;

                case "--apply":
                    if (mode == MigrationMode.Report)
                    {
                        return CliParseResult.Fail("Specify only one of --report or --apply.");
                    }

                    mode = MigrationMode.Apply;
                    break;

                case "--include":
                    if (i + 1 >= args.Count)
                    {
                        return CliParseResult.Fail("--include requires a glob value, for example --include *.cs.");
                    }

                    var includeValue = args[i + 1];

                    // A value that looks like an option (starts with '-') is almost certainly a
                    // forgotten glob, not a file pattern. Reject it with a clear message instead of
                    // silently treating "--apply" or "-foo" as a glob that matches nothing.
                    if (includeValue.StartsWith('-'))
                    {
                        return CliParseResult.Fail(
                            $"--include requires a glob value, but got '{includeValue}', which looks like an option. " +
                            "Pass a file-name glob such as --include *Validator.cs.");
                    }

                    includeGlob = includeValue;
                    i++;
                    break;

                default:
                    if (arg.StartsWith('-'))
                    {
                        return CliParseResult.Fail($"Unknown option '{arg}'.");
                    }

                    if (path is not null)
                    {
                        return CliParseResult.Fail("Specify exactly one path.");
                    }

                    path = arg;
                    break;
            }
        }

        if (path is null)
        {
            return CliParseResult.Fail("A path to a directory or .cs file is required.");
        }

        // Default to the safe mode when neither flag is given, so a bare invocation never writes.
        return CliParseResult.Ok(new CliOptions(path, mode ?? MigrationMode.Report, includeGlob));
    }

    private static bool HasHelpFlag(IReadOnlyList<string> args)
    {
        foreach (var arg in args)
        {
            if (arg is "--help" or "-h" or "-?")
            {
                return true;
            }
        }

        return false;
    }
}
