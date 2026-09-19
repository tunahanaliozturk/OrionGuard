namespace Moongazing.OrionGuard.Generators.Tests;

using Microsoft.CodeAnalysis;
using static GeneratorTestHarness;

/// <summary>
/// What the generator does with members and names it cannot translate one-to-one: a getter it cannot call,
/// and two types whose metadata names differ only in the punctuation the hint name escapes.
/// </summary>
public class GenerateValidatorAccessibilityTests
{
    [Fact]
    public void Generator_ShouldSkipAndReport_AnInheritedPropertyWithANonPublicGetter()
    {
        // The generated validator is a namespace-level class, so it cannot read a protected getter.
        // Emitting instance.Secret used to produce code that does not compile.
        const string source = """
            using Moongazing.OrionGuard.Attributes;
            using Moongazing.OrionGuard.Generators;

            namespace App
            {
                public class Base
                {
                    [NotNull] public string? Secret { protected get; set; }
                    [NotNull] public string? Name { get; set; }
                }

                [GenerateValidator]
                public class Derived : Base
                {
                }
            }
            """;

        var (_, run) = Run(source, new OrionGuardGenerator());
        var assembly = Compile(source, new OrionGuardGenerator());

        Assert.Contains(run.Diagnostics, d => d.Id == "OG0003" && d.GetMessage().Contains("Secret", StringComparison.Ordinal));
        var result = Validate(assembly, "App.DerivedValidator", New(assembly, "App.Derived"));
        Assert.Contains(result.Errors, e => e.ParameterName == "Name");
        Assert.DoesNotContain(result.Errors, e => e.ParameterName == "Secret");
    }

    [Fact]
    public void Generator_ShouldNotCollide_WhenANestedTypeAndATopLevelTypeSpellTheSameHintName()
    {
        // Metadata names "A+B" (nested) and "A_B" (top level) both became the hint name "A_B", and Roslyn
        // drops every generated source of a generator that reports a duplicate hint name.
        const string source = """
            using Moongazing.OrionGuard.Attributes;
            using Moongazing.OrionGuard.Generators;

            namespace App
            {
                public class A
                {
                    [GenerateValidator] public class B { [NotNull] public string? Nested { get; set; } }
                }

                [GenerateValidator] public class A_B { [NotNull] public string? TopLevel { get; set; } }
            }
            """;

        var (_, run) = Run(source, new OrionGuardGenerator());
        var assembly = Compile(source, new OrionGuardGenerator());

        Assert.DoesNotContain(run.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal(
            run.Results.Single().GeneratedSources.Select(s => s.HintName).Distinct().Count(),
            run.Results.Single().GeneratedSources.Length);
        Assert.Contains(Validate(assembly, "App.BValidator", New(assembly, "App.A+B")).Errors,
            e => e.ParameterName == "Nested" && e.ErrorCode == "NOT_NULL");
        Assert.Contains(Validate(assembly, "App.A_BValidator", New(assembly, "App.A_B")).Errors,
            e => e.ParameterName == "TopLevel" && e.ErrorCode == "NOT_NULL");
    }
}
