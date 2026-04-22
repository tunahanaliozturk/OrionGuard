using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Moongazing.OrionGuard.Generators.StronglyTypedIds;

namespace Moongazing.OrionGuard.Generators.Tests;

public class StronglyTypedIdGeneratorConditionalEfCoreTests
{
    private static GeneratorDriverRunResult RunGenerator(string source, bool includeEfCoreReference)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location));

        if (!includeEfCoreReference)
        {
            references = references.Where(a =>
                !a.GetName().Name!.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
        }

        var metadataRefs = references
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .ToList();

        // The test csproj has a PackageReference to Microsoft.EntityFrameworkCore, but a referenced
        // assembly is only loaded by the runtime when a type from it is actually used — on CI runners
        // the test process never touches EF Core otherwise, so AppDomain.CurrentDomain.GetAssemblies()
        // does not include it. Add the reference deterministically via typeof().Assembly.Location,
        // which both forces the load and yields the exact file path for the metadata reference.
        if (includeEfCoreReference)
        {
            var efCoreLocation = typeof(Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<,>).Assembly.Location;
            if (!metadataRefs.OfType<PortableExecutableReference>().Any(r =>
                    string.Equals(r.FilePath, efCoreLocation, StringComparison.OrdinalIgnoreCase)))
            {
                metadataRefs.Add(MetadataReference.CreateFromFile(efCoreLocation));
            }
        }

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            metadataRefs.ToArray(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new StronglyTypedIdGenerator().AsSourceGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGenerators(compilation);
        return driver.GetRunResult();
    }

    [Fact]
    public void Generator_ShouldSkipEfCoreConverter_WhenEfCoreNotReferenced()
    {
        const string source = """
            using Moongazing.OrionGuard.Domain.Primitives;

            namespace App
            {
                [StronglyTypedId<System.Guid>]
                public readonly partial struct TenantId { }
            }
            """;

        var result = RunGenerator(source, includeEfCoreReference: false);

        var efCoreConverter = result.Results
            .SelectMany(r => r.GeneratedSources)
            .FirstOrDefault(s => s.HintName.Contains("TenantIdEfCoreValueConverter"));

        Assert.Equal(default, efCoreConverter);

        // Partial body, JSON, TypeConverter must still be emitted.
        var partialBody = result.Results.SelectMany(r => r.GeneratedSources)
            .FirstOrDefault(s => s.HintName.Contains("TenantId.StronglyTypedId"));
        Assert.NotEqual(default, partialBody);

        var jsonConverter = result.Results.SelectMany(r => r.GeneratedSources)
            .FirstOrDefault(s => s.HintName.Contains("TenantIdJsonConverter"));
        Assert.NotEqual(default, jsonConverter);

        var typeConverter = result.Results.SelectMany(r => r.GeneratedSources)
            .FirstOrDefault(s => s.HintName.Contains("TenantIdTypeConverter"));
        Assert.NotEqual(default, typeConverter);
    }

    [Fact]
    public void Generator_ShouldEmitEfCoreConverter_WhenEfCoreReferenced()
    {
        const string source = """
            using Moongazing.OrionGuard.Domain.Primitives;

            namespace App
            {
                [StronglyTypedId<System.Guid>]
                public readonly partial struct TenantId { }
            }
            """;

        var result = RunGenerator(source, includeEfCoreReference: true);

        var efCoreConverter = result.Results
            .SelectMany(r => r.GeneratedSources)
            .FirstOrDefault(s => s.HintName.Contains("TenantIdEfCoreValueConverter"));

        Assert.NotEqual(default, efCoreConverter);
    }
}
