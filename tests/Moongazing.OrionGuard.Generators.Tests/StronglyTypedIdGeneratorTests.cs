namespace Moongazing.OrionGuard.Generators.Tests;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Moongazing.OrionGuard.Generators.StronglyTypedIds;

public class StronglyTypedIdGeneratorTests
{
    private static GeneratorDriverRunResult RunGenerator(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var referencesList = new List<MetadataReference>();
        var referencePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Collect all AppDomain assemblies
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic || string.IsNullOrEmpty(assembly.Location))
                continue;

            if (referencePaths.Add(assembly.Location))
            {
                referencesList.Add(MetadataReference.CreateFromFile(assembly.Location));
            }
        }

        // Ensure EF Core is included if it's available in the output directory
        var testBinDir = Path.GetDirectoryName(typeof(StronglyTypedIdGeneratorTests).Assembly.Location);
        if (testBinDir != null)
        {
            foreach (var efCoreFile in new[] { "Microsoft.EntityFrameworkCore.dll", "Microsoft.EntityFrameworkCore.Abstractions.dll" })
            {
                var efCorePath = Path.Combine(testBinDir, efCoreFile);
                if (File.Exists(efCorePath) && referencePaths.Add(efCorePath))
                {
                    referencesList.Add(MetadataReference.CreateFromFile(efCorePath));
                }
            }
        }

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            referencesList.ToArray(),
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
        // Ensure EF Core is loaded into AppDomain
        try
        {
            _ = typeof(Microsoft.EntityFrameworkCore.DbContext);
        }
        catch { /* Already loaded or not available */ }

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

    [Fact]
    public void Generator_ShouldEmitJsonConverter_ForGuidBackedId()
    {
        const string source = """
            using Moongazing.OrionGuard.Domain.Primitives;

            namespace App
            {
                [StronglyTypedId<System.Guid>]
                public readonly partial struct ProductId { }
            }
            """;

        var result = RunGenerator(source);

        var jsonSource = result.Results
            .SelectMany(r => r.GeneratedSources)
            .FirstOrDefault(s => s.HintName.Contains("ProductIdJsonConverter"));

        Assert.NotEqual(default, jsonSource);
        var text = jsonSource.SourceText.ToString();
        Assert.Contains("System.Text.Json.Serialization.JsonConverter<App.ProductId>", text);
        Assert.Contains("Read(", text);
        Assert.Contains("Write(", text);
    }

    [Fact]
    public void Generator_ShouldEmitTypeConverter_ForGuidBackedId()
    {
        const string source = """
            using Moongazing.OrionGuard.Domain.Primitives;

            namespace App
            {
                [StronglyTypedId<System.Guid>]
                public readonly partial struct InvoiceId { }
            }
            """;

        var result = RunGenerator(source);

        var tcSource = result.Results
            .SelectMany(r => r.GeneratedSources)
            .FirstOrDefault(s => s.HintName.Contains("InvoiceIdTypeConverter"));

        Assert.NotEqual(default, tcSource);
        var text = tcSource.SourceText.ToString();
        Assert.Contains("System.ComponentModel.TypeConverter", text);
        Assert.Contains("ConvertFrom", text);
        Assert.Contains("ConvertTo", text);
    }

    [Fact]
    public void Generator_ShouldEmitIStronglyTypedIdInterface_OnGeneratedStruct()
    {
        const string source = """
            using Moongazing.OrionGuard.Domain.Primitives;

            namespace App
            {
                [StronglyTypedId<System.Guid>]
                public readonly partial struct WarehouseId { }
            }
            """;

        var result = RunGenerator(source);

        var partialSource = result.Results
            .SelectMany(r => r.GeneratedSources)
            .FirstOrDefault(s => s.HintName.Contains("WarehouseId.StronglyTypedId"));

        Assert.NotEqual(default, partialSource);
        var text = partialSource.SourceText.ToString();
        Assert.Contains("global::Moongazing.OrionGuard.Domain.Primitives.IStronglyTypedId<global::System.Guid>", text);
    }
}
