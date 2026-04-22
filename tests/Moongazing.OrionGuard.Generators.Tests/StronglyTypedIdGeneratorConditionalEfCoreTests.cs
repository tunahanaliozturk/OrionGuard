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

        var metadataRefsList = references
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .ToList();

        // If including EF Core but it's not loaded in AppDomain, add it explicitly from NuGet cache
        if (includeEfCoreReference && !metadataRefsList.Any(mr => mr.Display?.Contains("EntityFrameworkCore") ?? false))
        {
            var efCoreAssemblyPath = System.IO.Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile),
                ".nuget", "packages", "microsoft.entityframeworkcore", "10.0.0", "lib", "net10.0", "Microsoft.EntityFrameworkCore.dll");
            if (System.IO.File.Exists(efCoreAssemblyPath))
            {
                metadataRefsList.Add(MetadataReference.CreateFromFile(efCoreAssemblyPath));
            }
        }

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            metadataRefsList.ToArray(),
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
