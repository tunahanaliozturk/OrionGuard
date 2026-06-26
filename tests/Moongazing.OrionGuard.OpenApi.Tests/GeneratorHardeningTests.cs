using System.Reflection;
using Microsoft.CodeAnalysis;
using Moongazing.OrionGuard.Core;

namespace Moongazing.OrionGuard.OpenApi.Tests;

/// <summary>
/// Regression tests for the generator-hardening fixes: a truncated document raises a clean diagnostic
/// instead of crashing; decimal and unsigned numeric members produce compiling, bound-enforcing
/// validators; an out-of-range integer keyword is diagnosed rather than wrapped; two same-named types in
/// different namespaces both generate without a hint-name collision; an ambiguous document name is
/// diagnosed; an annotated type outside the IValidator&lt;T&gt; contract is diagnosed; and an internal
/// target yields an internal validator that compiles.
/// </summary>
public class GeneratorHardeningTests
{
    private static bool HasDiagnostic(GeneratorRunResult run, string id) =>
        run.GeneratorDiagnostics.Any(d => d.Id == id);

    // -- Finding 1: truncated document raises a clean diagnostic, never crashes the generator. --

    [Fact]
    public void TruncatedDocument_ReportsDiagnostic_AndDoesNotCrash()
    {
        const string consumer = """
            using Moongazing.OrionGuard.DependencyInjection;
            namespace Sample
            {
                public sealed class Thing { public string Name { get; set; } = ""; }

                [Moongazing.OrionGuard.OpenApi.OpenApiValidator("thing.json", "#/components/schemas/Thing")]
                public partial class ThingValidator : IValidator<Thing> { }
            }
            """;

        // Document cut off mid-object: the bundled parser must surface a clean OG1002 (unparseable),
        // not throw an IndexOutOfRangeException out of the generator.
        const string truncated = """
            {
              "openapi": "3.0.3",
              "components": {
                "schemas": {
                  "Thing": {
                    "type": "object",
                    "properties": {
                      "name": { "type": "string"
            """;

        var run = GeneratorTestHarness.Run(consumer, "thing.json", truncated);

        // The run completed (no crash) and reported the unparseable-document diagnostic.
        Assert.True(HasDiagnostic(run, "OG1002"));

        // No generator crash surfaces as a generator-infrastructure error (e.g. CS8785).
        Assert.DoesNotContain(run.GeneratorDiagnostics, d => d.Id == "CS8785");
    }

    // -- Finding 2: decimal and unsigned members generate compiling, bound-enforcing validators. --

    private const string NumericConsumer = """
        using Moongazing.OrionGuard.DependencyInjection;
        namespace Sample
        {
            public sealed class Money
            {
                public decimal Amount { get; set; }
                public uint Quantity { get; set; }
                public ulong Big { get; set; }
            }

            [Moongazing.OrionGuard.OpenApi.OpenApiValidator("money.json", "#/components/schemas/Money")]
            public partial class MoneyValidator : IValidator<Money> { }
        }
        """;

    private const string NumericDocument = """
        {
          "openapi": "3.0.3",
          "info": { "title": "Money", "version": "1.0.0" },
          "components": {
            "schemas": {
              "Money": {
                "type": "object",
                "properties": {
                  "amount":   { "type": "number", "minimum": 0.5, "maximum": 1000.25 },
                  "quantity": { "type": "integer", "minimum": 1, "maximum": 100 },
                  "big":      { "type": "integer", "minimum": 0, "maximum": 9000000000 }
                }
              }
            }
          }
        }
        """;

    [Fact]
    public void DecimalAndUnsignedMembers_GenerateCompilingValidator()
    {
        // Compile asserts the generated code builds warning-clean under TreatWarningsAsErrors parity; a
        // double literal for a decimal bound, or a signed/over-range literal for an unsigned member, would
        // fail to compile here.
        var assembly = GeneratorTestHarness.Compile(NumericConsumer, "money.json", NumericDocument);

        Assert.NotNull(assembly.GetType("Sample.MoneyValidator"));
    }

    [Fact]
    public void DecimalBound_IsEnforced_AtRuntime()
    {
        var assembly = GeneratorTestHarness.Compile(NumericConsumer, "money.json", NumericDocument);
        var moneyType = assembly.GetType("Sample.Money")!;

        dynamic below = Activator.CreateInstance(moneyType)!;
        below.Amount = 0.25m;   // below minimum 0.5
        below.Quantity = 5u;
        below.Big = 10ul;

        var result = GeneratorTestHarness.Validate(assembly, "Sample.MoneyValidator", (object)below);
        Assert.Contains(result.Errors, e => e.ParameterName == "amount" && e.ErrorCode == "MINIMUM");
    }

    [Fact]
    public void UnsignedBound_IsEnforced_AtRuntime()
    {
        var assembly = GeneratorTestHarness.Compile(NumericConsumer, "money.json", NumericDocument);
        var moneyType = assembly.GetType("Sample.Money")!;

        dynamic over = Activator.CreateInstance(moneyType)!;
        over.Amount = 10m;
        over.Quantity = 101u;   // above maximum 100
        over.Big = 9000000001ul; // above maximum 9000000000 (exceeds int range; must not wrap)

        var result = GeneratorTestHarness.Validate(assembly, "Sample.MoneyValidator", (object)over);
        Assert.Contains(result.Errors, e => e.ParameterName == "quantity" && e.ErrorCode == "MAXIMUM");
        Assert.Contains(result.Errors, e => e.ParameterName == "big" && e.ErrorCode == "MAXIMUM");
    }

    // -- Finding 3: an out-of-range integer keyword is diagnosed, not silently wrapped. --

    [Fact]
    public void OutOfRangeIntegerKeyword_ReportsOG1007()
    {
        const string consumer = """
            using Moongazing.OrionGuard.DependencyInjection;
            namespace Sample
            {
                public sealed class Note { public string Text { get; set; } = ""; }

                [Moongazing.OrionGuard.OpenApi.OpenApiValidator("note.json", "#/components/schemas/Note")]
                public partial class NoteValidator : IValidator<Note> { }
            }
            """;

        // maxLength 5000000000 overflows int; the parser must record an issue and the generator raise
        // OG1007 rather than (int)-narrowing the value to a wrong, wrapped bound.
        const string document = """
            {
              "openapi": "3.0.3",
              "info": { "title": "Notes", "version": "1.0.0" },
              "components": {
                "schemas": {
                  "Note": {
                    "type": "object",
                    "properties": { "text": { "type": "string", "maxLength": 5000000000 } }
                  }
                }
              }
            }
            """;

        var run = GeneratorTestHarness.Run(consumer, "note.json", document);

        Assert.True(HasDiagnostic(run, "OG1007"));

        // The overflowed bound was skipped, not emitted as a wrapped MAX_LENGTH constraint.
        Assert.DoesNotContain("705032704", run.AllGeneratedText);

        // The build is still clean.
        var errors = run.OutputCompilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.True(errors.Count == 0, string.Join("\n", errors.Select(e => e.ToString())));
    }

    // -- Finding 6: two same-named types in different namespaces both generate (no hint-name collision). --

    [Fact]
    public void SameNamedValidators_InDifferentNamespaces_BothGenerate()
    {
        const string consumer = """
            using Moongazing.OrionGuard.DependencyInjection;

            namespace First
            {
                public sealed class Customer { public string Name { get; set; } = ""; }

                [Moongazing.OrionGuard.OpenApi.OpenApiValidator("customer.json", "#/components/schemas/Customer")]
                public partial class CustomerValidator : IValidator<Customer> { }
            }

            namespace Second
            {
                public sealed class Customer { public string Name { get; set; } = ""; }

                [Moongazing.OrionGuard.OpenApi.OpenApiValidator("customer.json", "#/components/schemas/Customer")]
                public partial class CustomerValidator : IValidator<Customer> { }
            }
            """;

        const string document = """
            {
              "openapi": "3.0.3",
              "info": { "title": "Customers", "version": "1.0.0" },
              "components": {
                "schemas": {
                  "Customer": {
                    "type": "object",
                    "properties": { "name": { "type": "string", "minLength": 1 } }
                  }
                }
              }
            }
            """;

        var assembly = GeneratorTestHarness.Compile(consumer, "customer.json", document);

        // Both validators were generated under distinct hint names and survive into the assembly.
        Assert.NotNull(assembly.GetType("First.CustomerValidator"));
        Assert.NotNull(assembly.GetType("Second.CustomerValidator"));
    }

    // -- Finding 5: an ambiguous AdditionalFile match is diagnosed (OG1009). --

    [Fact]
    public void AmbiguousDocument_ReportsOG1009()
    {
        const string consumer = """
            using Moongazing.OrionGuard.DependencyInjection;
            namespace Sample
            {
                public sealed class Thing { public string Name { get; set; } = ""; }

                [Moongazing.OrionGuard.OpenApi.OpenApiValidator("openapi.json", "#/components/schemas/Thing")]
                public partial class ThingValidator : IValidator<Thing> { }
            }
            """;

        const string doc = """
            {
              "openapi": "3.0.3",
              "info": { "title": "Sample", "version": "1.0.0" },
              "components": {
                "schemas": {
                  "Thing": { "type": "object", "properties": { "name": { "type": "string", "minLength": 1 } } }
                }
              }
            }
            """;

        // Two files share the bare name 'openapi.json' under different directories; the bare-name
        // resolution is ambiguous and must be reported rather than silently picking one.
        var run = GeneratorTestHarness.Run(consumer, new[]
        {
            ("api/v1/openapi.json", doc),
            ("api/v2/openapi.json", doc),
        });

        Assert.True(HasDiagnostic(run, "OG1009"));
    }

    [Fact]
    public void SubPathDocument_ResolvesUnambiguously_WhenAttributeQualifiesIt()
    {
        const string consumer = """
            using Moongazing.OrionGuard.DependencyInjection;
            namespace Sample
            {
                public sealed class Thing { public string Name { get; set; } = ""; }

                [Moongazing.OrionGuard.OpenApi.OpenApiValidator("v2/openapi.json", "#/components/schemas/Thing")]
                public partial class ThingValidator : IValidator<Thing> { }
            }
            """;

        const string doc = """
            {
              "openapi": "3.0.3",
              "info": { "title": "Sample", "version": "1.0.0" },
              "components": {
                "schemas": {
                  "Thing": { "type": "object", "properties": { "name": { "type": "string", "minLength": 1 } } }
                }
              }
            }
            """;

        // The attribute carries the 'v2/' sub-path, so it binds to the v2 file even though another
        // 'openapi.json' exists. No ambiguity, no missing-document diagnostic.
        var run = GeneratorTestHarness.Run(consumer, new[]
        {
            ("api/v1/openapi.json", doc),
            ("api/v2/openapi.json", doc),
        });

        Assert.False(HasDiagnostic(run, "OG1009"));
        Assert.False(HasDiagnostic(run, "OG1001"));
        Assert.Contains("MIN_LENGTH", run.AllGeneratedText);
    }

    // -- Finding 4: an annotated type outside the IValidator<T> contract is diagnosed (OG1008). --

    [Fact]
    public void TypeOutsideValidatorContract_ReportsOG1008_AndGeneratesNothing()
    {
        const string consumer = """
            namespace Sample
            {
                public sealed class Thing { public string Name { get; set; } = ""; }

                // Annotated but does not implement Moongazing.OrionGuard.DependencyInjection.IValidator<T>.
                [Moongazing.OrionGuard.OpenApi.OpenApiValidator("thing.json", "#/components/schemas/Thing")]
                public partial class NotAValidator { }
            }
            """;

        const string doc = """
            {
              "openapi": "3.0.3",
              "info": { "title": "Sample", "version": "1.0.0" },
              "components": {
                "schemas": {
                  "Thing": { "type": "object", "properties": { "name": { "type": "string", "minLength": 1 } } }
                }
              }
            }
            """;

        var run = GeneratorTestHarness.Run(consumer, "thing.json", doc);

        Assert.True(HasDiagnostic(run, "OG1008"));
        Assert.DoesNotContain("IValidator<", run.AllGeneratedText);
    }

    // -- Finding 7: an internal target yields an internal validator that compiles. --

    [Fact]
    public void InternalTarget_GeneratesInternalValidator_ThatCompiles()
    {
        const string consumer = """
            using Moongazing.OrionGuard.DependencyInjection;
            namespace Sample
            {
                internal sealed class Secret { public string Code { get; set; } = ""; }

                [Moongazing.OrionGuard.OpenApi.OpenApiValidator("secret.json", "#/components/schemas/Secret")]
                internal partial class SecretValidator : IValidator<Secret> { }
            }
            """;

        const string doc = """
            {
              "openapi": "3.0.3",
              "info": { "title": "Secret", "version": "1.0.0" },
              "components": {
                "schemas": {
                  "Secret": { "type": "object", "properties": { "code": { "type": "string", "minLength": 1 } } }
                }
              }
            }
            """;

        var run = GeneratorTestHarness.Run(consumer, "secret.json", doc);

        // The generated partial must repeat 'internal' so the two declarations agree (a 'public' partial
        // over an 'internal' user partial is CS0262).
        Assert.Contains("internal partial class SecretValidator", run.AllGeneratedText);
        Assert.DoesNotContain("public partial class SecretValidator", run.AllGeneratedText);

        // And it compiles cleanly (this is what would have broken before the accessibility fix).
        var assembly = GeneratorTestHarness.Compile(consumer, "secret.json", doc);
        Assert.NotNull(assembly.GetType("Sample.SecretValidator"));
    }
}
