namespace Moongazing.OrionGuard.Generators.Tests;

using Microsoft.CodeAnalysis;
using static GeneratorTestHarness;

/// <summary>
/// The generator translates OrionGuard attributes only, matched by full name, and reports (OG0002) an
/// OrionGuard <c>ValidationAttribute</c> it cannot translate instead of silently ignoring it.
/// </summary>
public class GenerateValidatorAttributeMatchingTests
{
    [Fact]
    public void Generator_ShouldIgnoreDataAnnotationsRange_WhenMatchingOrionGuardAttributes()
    {
        // Matching by simple name read DataAnnotations' [Range(1, 10)] as an OrionGuard range and
        // generated a RANGE check for it.
        const string source = """
            using Moongazing.OrionGuard.Generators;

            namespace App
            {
                [GenerateValidator]
                public sealed class Order
                {
                    [Moongazing.OrionGuard.Attributes.NotNull] public string? Name { get; set; } = "book";
                    [System.ComponentModel.DataAnnotations.Range(1, 10)] public int Quantity { get; set; } = 50;
                }
            }
            """;

        var assembly = Compile(source, new OrionGuardGenerator());

        var result = Validate(assembly, "App.OrderValidator", New(assembly, "App.Order"));

        Assert.True(result.IsValid, ErrorSummary(result));
    }

    [Fact]
    public void Generator_ShouldReportOG0002_ForAValidationAttributeItCannotTranslate()
    {
        const string source = """
            using Moongazing.OrionGuard.Attributes;
            using Moongazing.OrionGuard.Generators;

            namespace App
            {
                public sealed class PostcodeAttribute : ValidationAttribute
                {
                    public override bool IsValid(object? value) => value is string s && s.Length == 5;
                    protected override string GetDefaultMessage(string propertyName) => propertyName + " is not a postcode.";
                }

                [GenerateValidator]
                public sealed class Address
                {
                    [NotNull] public string? Street { get; set; }
                    [Postcode] public string? Zip { get; set; }
                }
            }
            """;

        var (output, run) = Run(source, new OrionGuardGenerator());

        var diagnostic = Assert.Single(run.Diagnostics, d => d.Id == "OG0002");
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("PostcodeAttribute", diagnostic.GetMessage());
        Assert.Contains("'Zip'", diagnostic.GetMessage());
        Assert.Equal("Postcode", source.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length));

        // The attributes the generator does understand are still generated.
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains(run.Results.Single().GeneratedSources, s => s.HintName == "App.AddressValidator.g.cs");
    }
}
