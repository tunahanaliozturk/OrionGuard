using Moongazing.OrionGuard.Migration;

namespace Moongazing.OrionGuard.Migration.Tests;

/// <summary>
/// Guards the fidelity of the <see cref="InMemoryFileSystem"/> test double against the production
/// <see cref="PhysicalFileSystem"/> contract: enumeration must respect directory boundaries (never
/// matching a sibling directory that shares a name prefix) and must honour the full <c>*</c>/<c>?</c>
/// glob -- not a bare suffix test. A test double that diverges here would let runner tests pass
/// against behaviour the real file system never exhibits.
/// </summary>
public sealed class InMemoryFileSystemTests
{
    [Fact]
    public void EnumerateFiles_DoesNotMatchSiblingDirectorySharingNamePrefix()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile("/repo/Foo.cs", "in scope");
        fs.AddFile("/repo2/Bar.cs", "sibling, must be excluded");

        var matches = fs.EnumerateFiles("/repo", "*.cs").ToList();

        Assert.Contains("/repo/Foo.cs", matches);
        Assert.DoesNotContain("/repo2/Bar.cs", matches);
    }

    [Fact]
    public void EnumerateFiles_RecursesIntoSubdirectories()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile("/repo/Top.cs", "top level");
        fs.AddFile("/repo/nested/Deep.cs", "nested");

        var matches = fs.EnumerateFiles("/repo", "*.cs").ToList();

        Assert.Contains("/repo/Top.cs", matches);
        Assert.Contains("/repo/nested/Deep.cs", matches);
    }

    [Fact]
    public void EnumerateFiles_HonoursQuestionMarkWildcard()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile("/repo/V1.cs", "single char");
        fs.AddFile("/repo/V12.cs", "two chars, must not match V?.cs");

        var matches = fs.EnumerateFiles("/repo", "V?.cs").ToList();

        Assert.Contains("/repo/V1.cs", matches);
        Assert.DoesNotContain("/repo/V12.cs", matches);
    }

    [Fact]
    public void EnumerateFiles_HonoursMidPatternWildcard()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile("/repo/OrderValidator.cs", "matches *Validator.cs");
        fs.AddFile("/repo/OrderValidatorFactory.cs", "does not match *Validator.cs");
        fs.AddFile("/repo/Helper.cs", "no Validator segment");

        var matches = fs.EnumerateFiles("/repo", "*Validator.cs").ToList();

        Assert.Contains("/repo/OrderValidator.cs", matches);
        Assert.DoesNotContain("/repo/OrderValidatorFactory.cs", matches);
        Assert.DoesNotContain("/repo/Helper.cs", matches);
    }

    [Fact]
    public void EnumerateFiles_MatchesFileNameOnly_NotPathSegments()
    {
        // The glob applies to the file name, never to directory segments: a `*.cs` pattern must not
        // be satisfied by a `.cs` appearing as a directory name.
        var fs = new InMemoryFileSystem();
        fs.AddFile("/repo/weird.cs/notes.txt", "directory named weird.cs");

        var matches = fs.EnumerateFiles("/repo", "*.cs").ToList();

        Assert.DoesNotContain("/repo/weird.cs/notes.txt", matches);
    }

    [Fact]
    public void EnumerateFiles_CaseSensitivity_MatchesRealFileSystem()
    {
        // The in-memory double must agree with the real PhysicalFileSystem on glob case-sensitivity,
        // which is the host platform default (case-insensitive on Windows, case-sensitive on a typical
        // Linux file system). A file written in lower case is searched for with an upper-case glob;
        // whether it is found must be identical between the two implementations on this machine.
        var root = Path.Combine(Path.GetTempPath(), "orionguard-case-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var lowerName = "casecheck.cs";
            File.WriteAllText(Path.Combine(root, lowerName), "// probe");

            var realMatched = new PhysicalFileSystem()
                .EnumerateFiles(root, "CASECHECK.CS")
                .Any();

            var inMemory = new InMemoryFileSystem();
            inMemory.AddFile(root.Replace('\\', '/') + "/" + lowerName, "// probe");
            var inMemoryMatched = inMemory.EnumerateFiles(root, "CASECHECK.CS").Any();

            Assert.Equal(realMatched, inMemoryMatched);
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup of the temp tree.
            }
        }
    }
}
