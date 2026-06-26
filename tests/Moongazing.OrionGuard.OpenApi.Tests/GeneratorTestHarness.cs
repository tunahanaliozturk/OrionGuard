using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Moongazing.OrionGuard.Core;
using Moongazing.OrionGuard.OpenApi;

namespace Moongazing.OrionGuard.OpenApi.Tests;

/// <summary>
/// In-memory test harness for the OpenAPI validator generator. It compiles a small consumer together
/// with an OpenAPI document supplied as an AdditionalFile, runs the generator, and (for behavioural
/// tests) emits the resulting assembly, loads it, and invokes the generated validator so each
/// constraint can be exercised against real input through the real <see cref="GuardResult"/> surface.
/// </summary>
internal sealed class AdditionalTextString : AdditionalText
{
    private readonly string _text;

    public AdditionalTextString(string path, string text)
    {
        Path = path;
        _text = text;
    }

    public override string Path { get; }

    public override SourceText GetText(CancellationToken cancellationToken = default) =>
        SourceText.From(_text, System.Text.Encoding.UTF8);
}

internal sealed class GeneratorRunResult
{
    public GeneratorRunResult(
        Compilation outputCompilation,
        ImmutableArray<Diagnostic> generatorDiagnostics,
        IReadOnlyList<string> generatedSources)
    {
        OutputCompilation = outputCompilation;
        GeneratorDiagnostics = generatorDiagnostics;
        GeneratedSources = generatedSources;
    }

    public Compilation OutputCompilation { get; }

    public ImmutableArray<Diagnostic> GeneratorDiagnostics { get; }

    public IReadOnlyList<string> GeneratedSources { get; }

    public string AllGeneratedText => string.Join("\n\n", GeneratedSources);
}

internal static class GeneratorTestHarness
{
    /// <summary>
    /// Runs the generator over <paramref name="consumerSource"/> with the given OpenAPI document mounted
    /// as an AdditionalFile, and returns the output compilation, generator diagnostics, and generated
    /// source texts.
    /// </summary>
    public static GeneratorRunResult Run(string consumerSource, string documentFileName, string documentJson) =>
        Run(consumerSource, new[] { (documentFileName, documentJson) });

    /// <summary>
    /// Runs the generator with several OpenAPI documents mounted as AdditionalFiles (file path + content),
    /// so a test can exercise document resolution across more than one candidate file. A path may include
    /// directory segments (for example <c>schemas/openapi.json</c>) to test sub-path resolution.
    /// </summary>
    public static GeneratorRunResult Run(
        string consumerSource, IReadOnlyList<(string path, string json)> documents)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(consumerSource, new CSharpParseOptions(LanguageVersion.Latest));

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .ToList();

        // Force the OrionGuard core assembly into the reference set deterministically: a referenced
        // assembly is only loaded when a type from it is touched, so on a clean test process
        // AppDomain.GetAssemblies() may not include it yet.
        var coreLocation = typeof(GuardResult).Assembly.Location;
        if (!references.OfType<PortableExecutableReference>()
                .Any(r => string.Equals(r.FilePath, coreLocation, StringComparison.OrdinalIgnoreCase)))
        {
            references.Add(MetadataReference.CreateFromFile(coreLocation));
        }

        var compilation = CSharpCompilation.Create(
            "OpenApiConsumerAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var generator = new OpenApiValidatorGenerator().AsSourceGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new[] { generator },
            additionalTexts: documents
                .Select(d => (AdditionalText)new AdditionalTextString(d.path, d.json))
                .ToImmutableArray());

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);

        var runResult = driver.GetRunResult();
        var generatorDiagnostics = runResult.Diagnostics;
        var generatedSources = runResult.Results
            .SelectMany(r => r.GeneratedSources)
            .Select(s => s.SourceText.ToString())
            .ToList();

        return new GeneratorRunResult(outputCompilation, generatorDiagnostics, generatedSources);
    }

    /// <summary>
    /// Runs the generator, asserts the output compiles cleanly (no errors, mirroring a consumer build),
    /// emits and loads the assembly, and returns it so a test can construct and invoke a validator.
    /// </summary>
    public static Assembly Compile(string consumerSource, string documentFileName, string documentJson)
    {
        var run = Run(consumerSource, documentFileName, documentJson);

        // A generator that reports an error diagnostic (e.g. an unresolved pointer or an ambiguous
        // document) produced no usable validator; surface it as a hard failure here instead of letting the
        // test proceed to a confusing "type not found" further down. Generator diagnostics live on the run
        // result, separate from the output compilation's own diagnostics.
        var generatorErrors = run.GeneratorDiagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        Assert.True(
            generatorErrors.Count == 0,
            "Generator reported error diagnostics:\n"
            + string.Join("\n", generatorErrors.Select(d => d.ToString())));

        // Treat warnings as errors to mirror a consumer building with <TreatWarningsAsErrors>true</TreatWarningsAsErrors>.
        // CS1701 (assembly version unification) is benign reference-assembly noise from the in-memory
        // reference set and is not something the generated code can cause, so it is excluded.
        var blocking = run.OutputCompilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error
                || (d.Severity == DiagnosticSeverity.Warning && d.Id != "CS1701" && d.Id != "CS1702"))
            .ToList();

        Assert.True(
            blocking.Count == 0,
            "Generated consumer assembly did not compile warning-clean (TreatWarningsAsErrors parity):\n"
            + string.Join("\n", blocking.Select(e => e.ToString()))
            + "\n\n--- Generated sources ---\n"
            + run.AllGeneratedText);

        using var peStream = new MemoryStream();
        var emitResult = run.OutputCompilation.Emit(peStream);
        Assert.True(
            emitResult.Success,
            "Emit failed:\n" + string.Join("\n", emitResult.Diagnostics.Select(d => d.ToString())));

        peStream.Seek(0, SeekOrigin.Begin);
        return Assembly.Load(peStream.ToArray());
    }

    /// <summary>
    /// Instantiates the generated validator and invokes <c>Validate</c> on the supplied instance,
    /// returning the real <see cref="GuardResult"/>.
    /// </summary>
    public static GuardResult Validate(Assembly assembly, string validatorTypeName, object instance)
    {
        var validatorType = assembly.GetType(validatorTypeName)
            ?? throw new InvalidOperationException($"Validator type '{validatorTypeName}' not found in generated assembly.");

        var validator = Activator.CreateInstance(validatorType)
            ?? throw new InvalidOperationException($"Could not instantiate '{validatorTypeName}'.");

        var validateMethod = validatorType.GetMethod("Validate", new[] { instance.GetType() })
            ?? validatorType.GetMethods().First(m => m.Name == "Validate" && m.GetParameters().Length == 1);

        var result = validateMethod.Invoke(validator, new[] { instance });
        return (GuardResult)result!;
    }
}
