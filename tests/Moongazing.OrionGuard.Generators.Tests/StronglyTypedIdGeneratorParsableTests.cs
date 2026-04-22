using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Moongazing.OrionGuard.Generators.StronglyTypedIds;

namespace Moongazing.OrionGuard.Generators.Tests;

public class StronglyTypedIdGeneratorParsableTests
{
    private static GeneratorDriverRunResult RunGenerator(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .ToArray();
        var compilation = CSharpCompilation.Create("TestAssembly", new[] { syntaxTree }, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var driver = CSharpGeneratorDriver.Create(new StronglyTypedIdGenerator().AsSourceGenerator());
        return driver.RunGenerators(compilation).GetRunResult();
    }

    [Fact]
    public void Generator_ShouldEmitParsableMembers_ForGuidBackedId()
    {
        const string source = """
            using Moongazing.OrionGuard.Domain.Primitives;

            namespace App
            {
                [StronglyTypedId<System.Guid>]
                public readonly partial struct ShipmentId { }
            }
            """;

        var result = RunGenerator(source);

        var parsable = result.Results.SelectMany(r => r.GeneratedSources)
            .FirstOrDefault(s => s.HintName.Contains("ShipmentId.Parsable"));

        Assert.NotEqual(default, parsable);
        var text = parsable.SourceText.ToString();

        Assert.Contains("public static ShipmentId Parse(string s, global::System.IFormatProvider? provider)", text);
        Assert.Contains("public static bool TryParse(string? s, global::System.IFormatProvider? provider, out ShipmentId result)", text);
        Assert.Contains("public static ShipmentId Parse(global::System.ReadOnlySpan<char> s, global::System.IFormatProvider? provider)", text);
        Assert.Contains("public static bool TryParse(global::System.ReadOnlySpan<char> s, global::System.IFormatProvider? provider, out ShipmentId result)", text);
        Assert.Contains("global::System.Guid.Parse", text);
        Assert.Contains("global::System.Guid.TryParse", text);
    }

    [Fact]
    public void Generator_ShouldEmitParsableMembers_ForIntBackedId()
    {
        const string source = """
            using Moongazing.OrionGuard.Domain.Primitives;

            namespace App
            {
                [StronglyTypedId<int>]
                public readonly partial struct BucketId { }
            }
            """;

        var result = RunGenerator(source);

        var parsable = result.Results.SelectMany(r => r.GeneratedSources)
            .FirstOrDefault(s => s.HintName.Contains("BucketId.Parsable"));

        Assert.NotEqual(default, parsable);
        var text = parsable.SourceText.ToString();
        Assert.Contains("int.Parse(s, global::System.Globalization.NumberStyles.Integer, provider)", text);
        Assert.Contains("int.TryParse(s, global::System.Globalization.NumberStyles.Integer, provider, out var v)", text);
    }

    [Fact]
    public void Generator_ShouldEmitParsableMembers_ForStringBackedId()
    {
        const string source = """
            using Moongazing.OrionGuard.Domain.Primitives;

            namespace App
            {
                [StronglyTypedId<string>]
                public readonly partial struct AccountCode { }
            }
            """;

        var result = RunGenerator(source);

        var parsable = result.Results.SelectMany(r => r.GeneratedSources)
            .FirstOrDefault(s => s.HintName.Contains("AccountCode.Parsable"));

        Assert.NotEqual(default, parsable);
        var text = parsable.SourceText.ToString();
        Assert.Contains("public static AccountCode Parse(string s, global::System.IFormatProvider? provider)", text);
        Assert.Contains("public static bool TryParse(string? s, global::System.IFormatProvider? provider, out AccountCode result)", text);
    }
}
