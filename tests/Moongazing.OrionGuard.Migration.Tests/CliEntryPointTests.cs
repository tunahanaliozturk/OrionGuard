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
    public void Run_BadArguments_PrintsErrorAndHelpWithUsageExitCode()
    {
        var writer = new StringWriter();
        var code = MigrationCli.Run(new[] { "frobnicate" }, writer);

        Assert.Equal(2, code);
        Assert.Contains("Unknown command", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("USAGE:", writer.ToString(), StringComparison.Ordinal);
    }
}
