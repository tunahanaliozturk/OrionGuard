using System.Text;
using Moongazing.OrionGuard.Migration;

namespace Moongazing.OrionGuard.Migration.Tests;

/// <summary>
/// Exercises <see cref="PhysicalFileSystem"/> against a real temporary directory: directory
/// enumeration (glob, boundaries, bin/obj exclusion), BOM-preserving round trips, and atomic
/// writes. Each test creates and deletes its own isolated temp tree.
/// </summary>
public sealed class PhysicalFileSystemTests : IDisposable
{
    private readonly string _root;
    private readonly PhysicalFileSystem _fs = new();

    public PhysicalFileSystemTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "orionguard-fs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup of the temp tree.
        }
    }

    [Fact]
    public void EnumerateFiles_ExcludesBinAndObjOutputDirectories()
    {
        WriteFile(Path.Combine(_root, "A.cs"), "// a");
        WriteFile(Path.Combine(_root, "src", "B.cs"), "// b");
        WriteFile(Path.Combine(_root, "bin", "Release", "Generated.cs"), "// generated");
        WriteFile(Path.Combine(_root, "obj", "Debug", "AssemblyInfo.cs"), "// objgen");
        WriteFile(Path.Combine(_root, "src", "obj", "Nested.cs"), "// nested obj");

        var files = _fs.EnumerateFiles(_root, "*.cs")
            .Select(p => Path.GetFileName(p))
            .OrderBy(static n => n, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(new[] { "A.cs", "B.cs" }, files);
    }

    [Fact]
    public void EnumerateFiles_RespectsGlob_AndDirectoryBoundaries()
    {
        WriteFile(Path.Combine(_root, "UserValidator.cs"), "// v");
        WriteFile(Path.Combine(_root, "Helper.cs"), "// h");
        WriteFile(Path.Combine(_root, "nested", "OrderValidator.cs"), "// v2");
        WriteFile(Path.Combine(_root, "notes.txt"), "irrelevant");

        var files = _fs.EnumerateFiles(_root, "*Validator.cs")
            .Select(p => Path.GetFileName(p))
            .OrderBy(static n => n, StringComparer.Ordinal)
            .ToList();

        // Glob matches file names only (never path segments), recurses into real subdirectories, and
        // excludes non-matching names.
        Assert.Equal(new[] { "OrderValidator.cs", "UserValidator.cs" }, files);
    }

    [Fact]
    public void WriteFile_PreservesUtf8Bom_RoundTrip()
    {
        var path = Path.Combine(_root, "Bom.cs");
        var bomEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        File.WriteAllText(path, "original", bomEncoding);

        var read = _fs.ReadFile(path);
        Assert.Equal("original", read.Text);
        Assert.Equal(3, read.Encoding.GetPreamble().Length);

        _fs.WriteFile(path, "migrated", read.Encoding);

        var rawBytes = File.ReadAllBytes(path);
        Assert.True(
            rawBytes.Length >= 3 && rawBytes[0] == 0xEF && rawBytes[1] == 0xBB && rawBytes[2] == 0xBF,
            "expected the UTF-8 BOM to survive the write");
        Assert.Equal("migrated", _fs.ReadFile(path).Text);
    }

    [Fact]
    public void WriteFile_BomlessUtf8_StaysBomless()
    {
        var path = Path.Combine(_root, "NoBom.cs");
        File.WriteAllText(path, "original", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var read = _fs.ReadFile(path);
        Assert.Empty(read.Encoding.GetPreamble());

        _fs.WriteFile(path, "migrated", read.Encoding);

        var rawBytes = File.ReadAllBytes(path);
        Assert.False(
            rawBytes.Length >= 3 && rawBytes[0] == 0xEF && rawBytes[1] == 0xBB && rawBytes[2] == 0xBF,
            "a BOM-less file must not gain a BOM");
    }

    [Fact]
    public void WriteFile_LeavesNoTempFileBehind()
    {
        var path = Path.Combine(_root, "Clean.cs");
        File.WriteAllText(path, "original");

        _fs.WriteFile(path, "migrated", new UTF8Encoding(false));

        var leftovers = Directory.GetFiles(_root)
            .Where(p => p.Contains("orionguard.tmp", StringComparison.Ordinal))
            .ToList();
        Assert.Empty(leftovers);
        Assert.Equal("migrated", File.ReadAllText(path));
    }

    private static void WriteFile(string path, string contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
    }
}
