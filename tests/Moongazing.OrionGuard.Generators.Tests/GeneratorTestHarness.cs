namespace Moongazing.OrionGuard.Generators.Tests;

using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Moongazing.OrionGuard.Core;

/// <summary>
/// Compiles a consumer source together with a generator, and for behavioural tests emits and loads the
/// resulting assembly so the generated code is executed, not just inspected.
/// </summary>
internal static class GeneratorTestHarness
{
    // Assemblies a consumer's generated code needs. Each is only loaded once a type from it is touched,
    // so they are added by type rather than trusting AppDomain.GetAssemblies() to already contain them.
    private static readonly Type[] RequiredReferences =
    {
        typeof(GuardResult),
        typeof(System.Text.Json.JsonSerializer),
        typeof(System.ComponentModel.TypeConverter),
        typeof(System.ComponentModel.TypeConverterAttribute),
        typeof(System.ComponentModel.DataAnnotations.RangeAttribute),
    };

    public static CSharpCompilation CreateCompilation(string source)
    {
        // Unique per compilation: Compile loads into the default load context, which holds one assembly
        // per name.
        string assemblyName = "GeneratorTest_" + Guid.NewGuid().ToString("N");

        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var references = new List<MetadataReference>();

        // EF Core is left out whatever other tests have loaded, so the compiled output never depends on
        // test order (the EF converter is only emitted when EF Core is referenced).
        var locations = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Where(a => !a.GetName().Name!.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal))
            .Select(a => a.Location)
            .Concat(RequiredReferences.Select(t => t.Assembly.Location));

        foreach (var location in locations)
        {
            if (paths.Add(location))
            {
                references.Add(MetadataReference.CreateFromFile(location));
            }
        }

        return CSharpCompilation.Create(
            assemblyName,
            new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest)) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
    }

    public static (Compilation Output, GeneratorDriverRunResult Run) Run(string source, IIncrementalGenerator generator)
    {
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator.AsSourceGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(CreateCompilation(source), out var output, out _);
        return (output, driver.GetRunResult());
    }

    /// <summary>
    /// Runs the generator, asserts the result compiles without errors or warnings (the consumer may build
    /// with TreatWarningsAsErrors), then emits and loads it.
    /// </summary>
    public static Assembly Compile(string source, IIncrementalGenerator generator)
    {
        var (output, run) = Run(source, generator);

        Assert.All(run.Results, r => Assert.Null(r.Exception));

        // CS0618 is the intended [Obsolete] warning of the deprecated [StronglyTypedId] attribute;
        // CS1701/CS1702 are reference-unification noise from the in-memory reference set.
        var blocking = output.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error
                || (d.Severity == DiagnosticSeverity.Warning && d.Id is not ("CS0618" or "CS1701" or "CS1702")))
            .ToList();

        Assert.True(
            blocking.Count == 0,
            "Generated code did not compile cleanly:\n"
            + string.Join("\n", blocking.Select(d => d.ToString()))
            + "\n\n--- Generated sources ---\n"
            + string.Join("\n\n", run.Results.SelectMany(r => r.GeneratedSources).Select(s => s.SourceText.ToString())));

        using var peStream = new MemoryStream();
        var emit = output.Emit(peStream);
        Assert.True(emit.Success, "Emit failed:\n" + string.Join("\n", emit.Diagnostics));

        // The default context, not Assembly.Load(byte[]): an assembly-qualified type name, which
        // TypeConverterAttribute stores, only resolves against an assembly the default context knows.
        peStream.Position = 0;
        return AssemblyLoadContext.Default.LoadFromStream(peStream);
    }

    /// <summary>Invokes the generated static <c>{Type}Validator.Validate</c> on a new instance.</summary>
    public static GuardResult Validate(Assembly assembly, string validatorTypeName, object instance)
    {
        var validator = assembly.GetType(validatorTypeName)
            ?? throw new InvalidOperationException($"Validator '{validatorTypeName}' was not generated.");

        return (GuardResult)validator.GetMethod("Validate")!.Invoke(null, new[] { instance })!;
    }

    public static object New(Assembly assembly, string typeName, params (string Property, object? Value)[] values)
    {
        var type = assembly.GetType(typeName) ?? throw new InvalidOperationException($"Type '{typeName}' not found.");
        var instance = Activator.CreateInstance(type)!;
        foreach (var (property, value) in values)
        {
            type.GetProperty(property)!.SetValue(instance, value);
        }

        return instance;
    }

    public static string ErrorSummary(GuardResult result) =>
        string.Join("; ", result.Errors.Select(e => $"{e.ParameterName}:{e.ErrorCode}"));
}
