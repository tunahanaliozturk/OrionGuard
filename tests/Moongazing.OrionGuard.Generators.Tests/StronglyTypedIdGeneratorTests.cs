using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Moongazing.OrionGuard.Generators.StronglyTypedIds;

namespace Moongazing.OrionGuard.Generators.Tests;

public class StronglyTypedIdGeneratorTests
{
    private static GeneratorDriverRunResult RunGenerator(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .ToArray();

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new StronglyTypedIdGenerator().AsSourceGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGenerators(compilation);
        return driver.GetRunResult();
    }

    [Fact]
    public void Generator_ShouldEmitPartialStructWithValueField_ForGuidBackedId()
    {
        const string source = """
            using Moongazing.OrionGuard.Domain.Primitives;

            namespace App
            {
                [StronglyTypedId<System.Guid>]
                public readonly partial struct OrderId { }
            }
            """;

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics);
        var generated = result.Results.SelectMany(r => r.GeneratedSources).ToArray();
        Assert.Contains(generated, s => s.HintName.Contains("OrderId"));
        var body = generated.First(s => s.HintName.Contains("OrderId")).SourceText.ToString();
        Assert.Contains("public readonly partial struct OrderId", body);
        Assert.Contains("global::System.Guid Value", body);
    }

    [Fact]
    public void Generator_ShouldEmitAttributeSourceIntoCompilation()
    {
        const string source = "namespace App { public class Empty { } }";

        var result = RunGenerator(source);

        var attributeSource = result.Results
            .SelectMany(r => r.GeneratedSources)
            .FirstOrDefault(s => s.HintName.Contains("StronglyTypedIdAttribute"));

        Assert.NotEqual(default, attributeSource);
        Assert.Contains("StronglyTypedIdAttribute", attributeSource.SourceText.ToString());
    }

    [Fact]
    public void Generator_ShouldEmitEfCoreValueConverter_ForGuidBackedId()
    {
        const string source = """
            using Moongazing.OrionGuard.Domain.Primitives;

            namespace App
            {
                [StronglyTypedId<System.Guid>]
                public readonly partial struct CustomerId { }
            }
            """;

        var result = RunGenerator(source);

        var converterSource = result.Results
            .SelectMany(r => r.GeneratedSources)
            .FirstOrDefault(s => s.HintName.Contains("CustomerIdEfCoreValueConverter"));

        Assert.NotEqual(default, converterSource);
        var text = converterSource.SourceText.ToString();
        Assert.Contains("CustomerIdEfCoreValueConverter", text);
        Assert.Contains("Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter", text);
    }
}
