using System.Text;
using Moongazing.OrionGuard.Migration;

namespace Moongazing.OrionGuard.Migration.Tests;

public sealed class MigrationRunnerTests
{
    private const string ValidatorSource =
        """
        using FluentValidation;
        namespace Sample;
        public class V : AbstractValidator<Model>
        {
            public V()
            {
                RuleFor(x => x.Name).NotEmpty().MaximumLength(50);
            }
        }
        """;

    private const string MixedSource =
        """
        using FluentValidation;
        namespace Sample;
        public class V : AbstractValidator<Model>
        {
            public V()
            {
                RuleFor(x => x.Name).NotEmpty();
                RuleFor(x => x.Total).ScalePrecision(2, 10);
            }
        }
        """;

    private static (MigrationExitCode Code, string Output, InMemoryFileSystem Fs) RunFile(
        string path, string source, MigrationMode mode, string include = "*.cs")
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile(path, source);
        var writer = new StringWriter();
        var runner = new MigrationRunner(fs, writer);

        var code = runner.Run(new CliOptions(path, mode, include));
        return (code, writer.ToString(), fs);
    }

    [Fact]
    public void Report_DoesNotWriteFiles()
    {
        var (code, output, fs) = RunFile("/repo/V.cs", ValidatorSource, MigrationMode.Report);

        Assert.Equal(0, fs.WriteCount);
        Assert.Equal(ValidatorSource, fs.Files["/repo/V.cs"].Text);
        Assert.Equal(MigrationExitCode.Success, code);
        Assert.Contains("--- /repo/V.cs", output, StringComparison.Ordinal);
        Assert.Contains("Mode:           report", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_WritesMigratedFile()
    {
        var (code, output, fs) = RunFile("/repo/V.cs", ValidatorSource, MigrationMode.Apply);

        Assert.Equal(1, fs.WriteCount);
        Assert.Contains("FluentStyleValidator<Model>", fs.Files["/repo/V.cs"].Text, StringComparison.Ordinal);
        Assert.DoesNotContain("AbstractValidator", fs.Files["/repo/V.cs"].Text, StringComparison.Ordinal);
        Assert.Equal(MigrationExitCode.Success, code);
        Assert.Contains("migrated: /repo/V.cs", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_WithUnsupportedConstruct_ReturnsManualFollowUpExitCode()
    {
        var (code, output, _) = RunFile("/repo/V.cs", MixedSource, MigrationMode.Report);

        Assert.Equal(MigrationExitCode.ManualFollowUpRequired, code);
        Assert.Contains("Manual follow-ups: 1", output, StringComparison.Ordinal);
        Assert.Contains("ScalePrecision", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_WithUnsupportedConstruct_StillWritesPartialMigrationAndReportsExitCode()
    {
        var (code, _, fs) = RunFile("/repo/V.cs", MixedSource, MigrationMode.Apply);

        Assert.Equal(1, fs.WriteCount);
        Assert.Contains("FluentStyleValidator<Model>", fs.Files["/repo/V.cs"].Text, StringComparison.Ordinal);
        Assert.Contains("// TODO: OrionGuard migration - ScalePrecision", fs.Files["/repo/V.cs"].Text, StringComparison.Ordinal);
        Assert.Equal(MigrationExitCode.ManualFollowUpRequired, code);
    }

    [Fact]
    public void Run_MissingPath_ReturnsUsageError()
    {
        var fs = new InMemoryFileSystem();
        var writer = new StringWriter();
        var runner = new MigrationRunner(fs, writer);

        var code = runner.Run(new CliOptions("/does/not/exist", MigrationMode.Report, "*.cs"));

        Assert.Equal(MigrationExitCode.UsageError, code);
        Assert.Contains("Path not found", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_PreservesOriginalEncoding_KeepsUtf8Bom()
    {
        var fs = new InMemoryFileSystem();
        var bomEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        fs.AddFile("/repo/V.cs", ValidatorSource, bomEncoding);
        var runner = new MigrationRunner(fs, new StringWriter());

        runner.Run(new CliOptions("/repo/V.cs", MigrationMode.Apply, "*.cs"));

        // The migrated file must be written back in the SAME encoding it was read in: a UTF-8 file
        // with a BOM stays a UTF-8 file with a BOM, never silently switching to BOM-less.
        var written = fs.Files["/repo/V.cs"].Encoding;
        Assert.True(written.GetPreamble().Length == 3, "expected the UTF-8 BOM to be preserved");
        Assert.IsType<UTF8Encoding>(written);
    }

    [Fact]
    public void Apply_PreservesOriginalEncoding_KeepsBomlessUtf8()
    {
        var fs = new InMemoryFileSystem();
        var bomless = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        fs.AddFile("/repo/V.cs", ValidatorSource, bomless);
        var runner = new MigrationRunner(fs, new StringWriter());

        runner.Run(new CliOptions("/repo/V.cs", MigrationMode.Apply, "*.cs"));

        var written = fs.Files["/repo/V.cs"].Encoding;
        Assert.Empty(written.GetPreamble());
    }

    [Fact]
    public void Apply_WriteFailure_DoesNotCorruptFile_AndReports()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile("/repo/V.cs", ValidatorSource);
        fs.FailWriteFor("/repo/V.cs");
        var writer = new StringWriter();
        var runner = new MigrationRunner(fs, writer);

        var code = runner.Run(new CliOptions("/repo/V.cs", MigrationMode.Apply, "*.cs"));

        // The original source is intact (atomic write never touches the target on failure), the
        // failure is reported, and the run exits non-zero rather than crashing.
        Assert.Equal(ValidatorSource, fs.Files["/repo/V.cs"].Text);
        Assert.Equal(MigrationExitCode.ManualFollowUpRequired, code);
        Assert.Contains("failed to write: /repo/V.cs", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Run_Directory_MigratesMatchingFilesOnly()
    {
        var fs = new InMemoryFileSystem();
        fs.AddDirectory("/repo");
        fs.AddFile("/repo/AValidator.cs", ValidatorSource);
        fs.AddFile("/repo/notes.txt", "irrelevant");
        var writer = new StringWriter();
        var runner = new MigrationRunner(fs, writer);

        var code = runner.Run(new CliOptions("/repo", MigrationMode.Apply, "*.cs"));

        Assert.Equal(MigrationExitCode.Success, code);
        Assert.Contains("FluentStyleValidator", fs.Files["/repo/AValidator.cs"].Text, StringComparison.Ordinal);
        Assert.Equal("irrelevant", fs.Files["/repo/notes.txt"].Text);
    }
}
