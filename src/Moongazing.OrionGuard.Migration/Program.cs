using Moongazing.OrionGuard.Migration;

return MigrationCli.Run(args, Console.Out);

/// <summary>
/// The console entry point. Kept thin: it parses arguments, prints help or errors, and delegates
/// the actual work to <see cref="MigrationRunner"/> so the behaviour is testable without a process.
/// </summary>
internal static class MigrationCli
{
    public static int Run(string[] args, TextWriter output)
    {
        var parse = CliParser.Parse(args);

        if (parse.HelpRequested)
        {
            output.Write(HelpText);
            return (int)MigrationExitCode.Success;
        }

        if (parse.Error is not null)
        {
            output.WriteLine(parse.Error);
            output.WriteLine();
            output.Write(HelpText);
            return (int)MigrationExitCode.UsageError;
        }

        var runner = new MigrationRunner(new PhysicalFileSystem(), output);
        return (int)runner.Run(parse.Options!);
    }

    private const string HelpText =
        """
        orionguard - FluentValidation to OrionGuard migration codemod

        USAGE:
          dotnet orionguard migrate <path> [--report | --apply] [--include <glob>]

        ARGUMENTS:
          <path>            A directory to scan recursively, or a single .cs file.

        OPTIONS:
          --report          Print the proposed diff and report without writing files.
          --dry-run         Alias for --report.
          --apply           Write the migrated files in place.
          --include <glob>  File-name glob used when <path> is a directory (default *.cs).
          -h, --help        Show this help.

        BEHAVIOUR:
          Finds classes deriving from AbstractValidator<T> and rewrites their RuleFor chains
          onto the OrionGuard FluentStyleValidator compatibility surface. Any rule or construct
          with no safe equivalent (for example SetValidator, RuleForEach, ScalePrecision, async
          predicates) is left untouched, marked with a TODO comment, and listed in the summary.

          If neither --report nor --apply is given, the tool defaults to --report so it never
          writes by accident.

        EXIT CODES:
          0  Migration completed; nothing needs manual follow-up.
          1  Migration ran but at least one construct needs manual follow-up.
          2  Usage error (bad arguments or a path that does not exist).

        """;
}
