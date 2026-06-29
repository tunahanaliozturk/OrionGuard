namespace Moongazing.OrionGuard.Migration.Tests;

/// <summary>
/// Exercises the thin console entry point (<c>MigrationCli.Run</c>) for help and usage-error paths.
/// Reachable through InternalsVisibleTo.
/// </summary>
public sealed class CliEntryPointTests
{
    [Fact]
    public void Run_NoArgs_PrintsHelpAndSucceeds()
    {
        var writer = new StringWriter();
        var code = MigrationCli.Run(Array.Empty<string>(), writer);

        Assert.Equal(0, code);
        Assert.Contains("dotnet orionguard migrate", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Run_Help_AdvertisesInstalledDotnetToolCommand()
    {
        // The tool is packaged with ToolCommandName 'orionguard', so the real invocation is
        // 'dotnet orionguard ...'. The help header and examples must advertise that command, not a
        // bare 'orionguard', so users copy the command that actually works once installed.
        var writer = new StringWriter();
        var code = MigrationCli.Run(new[] { "--help" }, writer);
        var help = writer.ToString();

        Assert.Equal(0, code);
        Assert.Contains("dotnet orionguard - FluentValidation", help, StringComparison.Ordinal);
        Assert.Contains("dotnet orionguard migrate ./src --report", help, StringComparison.Ordinal);
        Assert.Contains("dotnet orionguard migrate ./src --apply", help, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_BadArguments_PrintsErrorAndHelpWithUsageExitCode()
    {
        var writer = new StringWriter();
        var code = MigrationCli.Run(new[] { "frobnicate" }, writer);

        Assert.Equal(2, code);
        Assert.Contains("Unknown command", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("USAGE:", writer.ToString(), StringComparison.Ordinal);
    }
}
