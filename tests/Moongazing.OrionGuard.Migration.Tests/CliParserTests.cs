using Moongazing.OrionGuard.Migration;

namespace Moongazing.OrionGuard.Migration.Tests;

public sealed class CliParserTests
{
    [Fact]
    public void Parse_NoArgs_RequestsHelp()
    {
        var result = CliParser.Parse(Array.Empty<string>());
        Assert.True(result.HelpRequested);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("-?")]
    public void Parse_HelpFlag_RequestsHelp(string flag)
    {
        var result = CliParser.Parse(new[] { "migrate", "./src", flag });
        Assert.True(result.HelpRequested);
    }

    [Fact]
    public void Parse_MigrateWithPath_DefaultsToReportMode()
    {
        var result = CliParser.Parse(new[] { "migrate", "./src" });

        Assert.NotNull(result.Options);
        Assert.Equal("./src", result.Options!.Path);
        Assert.Equal(MigrationMode.Report, result.Options.Mode);
        Assert.Equal("*.cs", result.Options.IncludeGlob);
    }

    [Theory]
    [InlineData("--report", MigrationMode.Report)]
    [InlineData("--dry-run", MigrationMode.Report)]
    [InlineData("--apply", MigrationMode.Apply)]
    public void Parse_ModeFlag_IsHonoured(string flag, MigrationMode expected)
    {
        var result = CliParser.Parse(new[] { "migrate", "./src", flag });
        Assert.Equal(expected, result.Options!.Mode);
    }

    [Fact]
    public void Parse_Include_CapturesGlob()
    {
        var result = CliParser.Parse(new[] { "migrate", "./src", "--include", "*Validator.cs" });
        Assert.Equal("*Validator.cs", result.Options!.IncludeGlob);
    }

    [Fact]
    public void Parse_BothReportAndApply_IsError()
    {
        var result = CliParser.Parse(new[] { "migrate", "./src", "--report", "--apply" });
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Parse_UnknownCommand_IsError()
    {
        var result = CliParser.Parse(new[] { "frobnicate", "./src" });
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Parse_NoPath_IsError()
    {
        var result = CliParser.Parse(new[] { "migrate", "--apply" });
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Parse_IncludeWithoutValue_IsError()
    {
        var result = CliParser.Parse(new[] { "migrate", "./src", "--include" });
        Assert.NotNull(result.Error);
    }

    [Theory]
    [InlineData("-foo")]
    [InlineData("--apply")]
    [InlineData("--report")]
    public void Parse_IncludeWithOptionLikeValue_IsError(string value)
    {
        // A value starting with '-' is almost certainly a forgotten glob, not a file pattern. It must
        // be rejected with a clear error rather than silently treated as a glob that matches nothing.
        var result = CliParser.Parse(new[] { "migrate", "./src", "--include", value });

        Assert.Null(result.Options);
        Assert.NotNull(result.Error);
        Assert.Contains("--include", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_UnknownOption_IsError()
    {
        var result = CliParser.Parse(new[] { "migrate", "./src", "--frob" });
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Parse_TwoPaths_IsError()
    {
        var result = CliParser.Parse(new[] { "migrate", "./a", "./b" });
        Assert.NotNull(result.Error);
    }
}
