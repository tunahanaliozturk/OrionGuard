namespace Moongazing.OrionGuard.Generators.Tests;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

/// <summary>
/// The generator emits validation code into consumer assemblies, so its regex checks must use
/// the core package's timeout-bounded regexes rather than the static, timeout-less
/// <c>Regex.IsMatch</c>.
/// </summary>
public class GenerateValidatorRegexTimeoutTests
{
    [Fact]
    public void GeneratedValidator_ShouldUseTimeoutBoundedRegexes_ForEmailAndPatternRules()
    {
        const string source = """
            using Moongazing.OrionGuard.Attributes;
            using Moongazing.OrionGuard.Generators;

            namespace App
            {
                [GenerateValidator]
                public sealed class SignupRequest
                {
                    [Email] public string? Email { get; set; }
                    [Regex("^[A-Z]{2}\"?$")] public string? CountryCode { get; set; }
                }
            }
            """;

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => (MetadataReference)MetadataReference.CreateFromFile(a.Location))
            .Append(MetadataReference.CreateFromFile(typeof(Moongazing.OrionGuard.Core.RegexCache).Assembly.Location))
            .ToList();

        var compilation = CSharpCompilation.Create(
            "RegexTimeoutTestAssembly",
            new[] { CSharpSyntaxTree.ParseText(source) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        CSharpGeneratorDriver
            .Create(new OrionGuardGenerator().AsSourceGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out _);

        var generated = string.Join(
            Environment.NewLine,
            updatedCompilation.SyntaxTrees.Skip(1).Select(t => t.ToString()));

        Assert.Contains("MatchesWithinTimeout(Moongazing.OrionGuard.Utilities.GeneratedRegexPatterns.Email(), instance.Email)", generated);
        Assert.Contains("MatchesWithinTimeout(Moongazing.OrionGuard.Core.RegexCache.GetOrCreate(@\"^[A-Z]{2}\"\"?$\"), instance.CountryCode)", generated);
        Assert.DoesNotContain("Regex.IsMatch(", generated);
        Assert.Empty(updatedCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public void GeneratedValidator_ShouldReportAValidationError_WhenAPatternMatchTimesOut()
    {
        // A nested quantifier backtracks exponentially on a near-miss; RegexCache stops it after one
        // second, and the generated validator used to let that RegexMatchTimeoutException escape.
        const string source = """
            using Moongazing.OrionGuard.Attributes;
            using Moongazing.OrionGuard.Generators;

            namespace App
            {
                [GenerateValidator]
                public sealed class Slug
                {
                    [Regex("^(a|aa)+$")] public string? Value { get; set; }
                }
            }
            """;

        var assembly = GeneratorTestHarness.Compile(source, new OrionGuardGenerator());

        var hostile = GeneratorTestHarness.New(assembly, "App.Slug", ("Value", new string('a', 64) + "!"));
        var result = GeneratorTestHarness.Validate(assembly, "App.SlugValidator", hostile);

        Assert.Contains(result.Errors, e => e.ParameterName == "Value" && e.ErrorCode == "REGEX");
    }
}
