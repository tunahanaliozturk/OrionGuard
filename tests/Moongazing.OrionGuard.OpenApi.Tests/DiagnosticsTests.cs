using Microsoft.CodeAnalysis;

namespace Moongazing.OrionGuard.OpenApi.Tests;

/// <summary>
/// Tests that the generator reports a clean OG-prefixed diagnostic (and never crashes the build) for
/// each failure mode: a missing document, an unparseable document, an unresolvable pointer, a
/// non-partial target, and an unsupported construct.
/// </summary>
public class DiagnosticsTests
{
    private const string ValidDocument = """
        {
          "openapi": "3.0.3",
          "info": { "title": "Sample", "version": "1.0.0" },
          "components": {
            "schemas": {
              "Thing": {
                "type": "object",
                "properties": { "name": { "type": "string", "minLength": 1 } }
              }
            }
          }
        }
        """;

    private static bool HasDiagnostic(GeneratorRunResult run, string id) =>
        run.GeneratorDiagnostics.Any(d => d.Id == id);

    [Fact]
    public void MissingAdditionalFile_ReportsOG1001()
    {
        const string consumer = """
            using Moongazing.OrionGuard.DependencyInjection;
            namespace Sample
            {
                public sealed class Thing { public string Name { get; set; } = ""; }

                [Moongazing.OrionGuard.OpenApi.OpenApiValidator("absent.json", "#/components/schemas/Thing")]
                public partial class ThingValidator : IValidator<Thing> { }
            }
            """;

        // The mounted file has a different name than the attribute asks for.
        var run = GeneratorTestHarness.Run(consumer, "present.json", ValidDocument);

        Assert.True(HasDiagnostic(run, "OG1001"));
    }

    [Fact]
    public void UnparseableDocument_ReportsOG1002()
    {
        const string consumer = """
            using Moongazing.OrionGuard.DependencyInjection;
            namespace Sample
            {
                public sealed class Thing { public string Name { get; set; } = ""; }

                [Moongazing.OrionGuard.OpenApi.OpenApiValidator("broken.json", "#/components/schemas/Thing")]
                public partial class ThingValidator : IValidator<Thing> { }
            }
            """;

        // YAML content is not valid JSON; the parser rejects it and the generator raises OG1002.
        const string yaml = "openapi: 3.0.3\ncomponents:\n  schemas:\n    Thing:\n      type: object\n";
        var run = GeneratorTestHarness.Run(consumer, "broken.json", yaml);

        Assert.True(HasDiagnostic(run, "OG1002"));
    }

    [Fact]
    public void UnresolvablePointer_ReportsOG1003()
    {
        const string consumer = """
            using Moongazing.OrionGuard.DependencyInjection;
            namespace Sample
            {
                public sealed class Thing { public string Name { get; set; } = ""; }

                [Moongazing.OrionGuard.OpenApi.OpenApiValidator("thing.json", "#/components/schemas/DoesNotExist")]
                public partial class ThingValidator : IValidator<Thing> { }
            }
            """;

        var run = GeneratorTestHarness.Run(consumer, "thing.json", ValidDocument);

        Assert.True(HasDiagnostic(run, "OG1003"));
    }

    [Fact]
    public void NonPartialTarget_ReportsOG1005()
    {
        const string consumer = """
            using Moongazing.OrionGuard.DependencyInjection;
            namespace Sample
            {
                public sealed class Thing { public string Name { get; set; } = ""; }

                [Moongazing.OrionGuard.OpenApi.OpenApiValidator("thing.json", "#/components/schemas/Thing")]
                public class ThingValidator { }
            }
            """;

        var run = GeneratorTestHarness.Run(consumer, "thing.json", ValidDocument);

        Assert.True(HasDiagnostic(run, "OG1005"));
    }

    [Fact]
    public void UnsupportedConstruct_ReportsOG1006_AndStillEnforcesRest()
    {
        const string consumer = """
            using Moongazing.OrionGuard.DependencyInjection;
            namespace Sample
            {
                public sealed class Pet { public string Name { get; set; } = ""; }

                [Moongazing.OrionGuard.OpenApi.OpenApiValidator("pet.json", "#/components/schemas/Pet")]
                public partial class PetValidator : IValidator<Pet> { }
            }
            """;

        // The Pet schema declares a discriminator (polymorphism), which is deferred. The generator must
        // warn via OG1006 yet still emit the supported 'name' minLength constraint.
        const string petDocument = """
            {
              "openapi": "3.0.3",
              "info": { "title": "Pets", "version": "1.0.0" },
              "components": {
                "schemas": {
                  "Pet": {
                    "type": "object",
                    "discriminator": { "propertyName": "petType" },
                    "properties": { "name": { "type": "string", "minLength": 2 } }
                  }
                }
              }
            }
            """;

        var run = GeneratorTestHarness.Run(consumer, "pet.json", petDocument);

        Assert.True(HasDiagnostic(run, "OG1006"));

        // The validator was still generated and the supported constraint is present.
        Assert.Contains("MIN_LENGTH", run.AllGeneratedText);

        // And the output compilation is still error-free (build never breaks).
        var errors = run.OutputCompilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.True(errors.Count == 0, string.Join("\n", errors.Select(e => e.ToString())));
    }

    [Fact]
    public void NoGeneratorDiagnostics_ForValidInput()
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

        var run = GeneratorTestHarness.Run(consumer, "thing.json", ValidDocument);

        Assert.Empty(run.GeneratorDiagnostics);
    }
}
