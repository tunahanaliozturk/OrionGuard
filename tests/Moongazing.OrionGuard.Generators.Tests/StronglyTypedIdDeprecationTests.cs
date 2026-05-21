namespace Moongazing.OrionGuard.Generators.Tests;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Moongazing.OrionGuard.Generators.StronglyTypedIds;

public class StronglyTypedIdDeprecationTests
{
    [Fact]
    public void StronglyTypedIdAttribute_ShouldRaiseCS0618_WhenApplied()
    {
        const string source = """
            using Moongazing.OrionGuard.Domain.Primitives;

            namespace App
            {
                [StronglyTypedId<System.Guid>]
                public readonly partial struct OrderId { }
            }
            """;

        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => (MetadataReference)MetadataReference.CreateFromFile(a.Location))
            .ToList();

        var compilation = CSharpCompilation.Create(
            "DeprecationTestAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        CSharpGeneratorDriver
            .Create(new StronglyTypedIdGenerator().AsSourceGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out _);

        var cs0618 = updatedCompilation.GetDiagnostics()
            .Where(d => d.Id == "CS0618")
            .ToArray();

        Assert.NotEmpty(cs0618);
        Assert.Contains(cs0618, d => d.GetMessage().Contains("OrionKey"));
    }
}
