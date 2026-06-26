using Microsoft.CodeAnalysis;
using Moongazing.OrionGuard.Core;

namespace Moongazing.OrionGuard.OpenApi.Tests;

/// <summary>
/// Tests that a nested <c>[OpenApiValidator]</c> target generates a partial inside the correct enclosing
/// type (so it extends the user's actual type and compiles), that every enclosing-type kind is reopened
/// with its own keyword (class / struct / record / record struct / interface, including a deep mixed-kind
/// chain), that the full declaring-type path is carried into the hint name so two same-named validators
/// nested in different outer types in the same namespace do not collide, and that an unsupported target
/// (generic, non-partial anywhere in the chain) is diagnosed (OG1010 / OG1011) and generates nothing.
/// "Generates nothing" is asserted directly via <c>ValidatorSourceCount == 0</c> (no validator source was
/// added), not by probing the concatenated generated text for an absent substring.
/// </summary>
public class NestedTargetTests
{
    private const string Document = """
        {
          "openapi": "3.0.3",
          "info": { "title": "Customers", "version": "1.0.0" },
          "components": {
            "schemas": {
              "Customer": {
                "type": "object",
                "required": [ "name" ],
                "properties": { "name": { "type": "string", "minLength": 3 } }
              }
            }
          }
        }
        """;

    private static bool HasDiagnostic(GeneratorRunResult run, string id) =>
        run.GeneratorDiagnostics.Any(d => d.Id == id);

    // -- Nested one level deep: compiles, nested correctly, and enforces the constraint. --

    [Fact]
    public void ValidatorNestedOneLevel_CompilesNestedCorrectly_AndEnforcesConstraint()
    {
        const string consumer = """
            using Moongazing.OrionGuard.DependencyInjection;
            namespace Sample
            {
                public sealed class Customer { public string Name { get; set; } = ""; }

                public partial class Outer
                {
                    [Moongazing.OrionGuard.OpenApi.OpenApiValidator("customer.json", "#/components/schemas/Customer")]
                    public partial class CustomerValidator : IValidator<Customer> { }
                }
            }
            """;

        var run = GeneratorTestHarness.Run(consumer, "customer.json", Document);

        // The generated partial reopens the outer type as a partial and nests the validator inside it.
        Assert.Contains("partial class Outer", run.AllGeneratedText);
        Assert.Equal(1, run.ValidatorSourceCount);

        var assembly = GeneratorTestHarness.Compile(consumer, "customer.json", Document);

        // The nested validator type is reachable by its full nested name and enforces minLength 3.
        var validatorType = assembly.GetType("Sample.Outer+CustomerValidator");
        Assert.NotNull(validatorType);

        var customerType = assembly.GetType("Sample.Customer")!;
        dynamic instance = Activator.CreateInstance(customerType)!;
        instance.Name = "ab"; // below minLength 3

        var result = GeneratorTestHarness.Validate(
            assembly, "Sample.Outer+CustomerValidator", (object)instance);
        Assert.Contains(result.Errors, e => e.ParameterName == "name" && e.ErrorCode == "MIN_LENGTH");
    }

    // -- Nested two levels deep: compiles. --

    [Fact]
    public void ValidatorNestedTwoLevels_Compiles()
    {
        const string consumer = """
            using Moongazing.OrionGuard.DependencyInjection;
            namespace Sample
            {
                public sealed class Customer { public string Name { get; set; } = ""; }

                public partial class Outer
                {
                    public partial struct Middle
                    {
                        [Moongazing.OrionGuard.OpenApi.OpenApiValidator("customer.json", "#/components/schemas/Customer")]
                        public partial class CustomerValidator : IValidator<Customer> { }
                    }
                }
            }
            """;

        // Both enclosing types must be reopened with the right keyword (class then struct) so the partial
        // agrees with the user's declarations; a wrong keyword or missing nesting would fail to compile.
        var run = GeneratorTestHarness.Run(consumer, "customer.json", Document);
        Assert.Contains("partial class Outer", run.AllGeneratedText);
        Assert.Contains("partial struct Middle", run.AllGeneratedText);
        Assert.Equal(1, run.ValidatorSourceCount);

        var assembly = GeneratorTestHarness.Compile(consumer, "customer.json", Document);
        Assert.NotNull(assembly.GetType("Sample.Outer+Middle+CustomerValidator"));
    }

    // -- Two same-named validators nested in different outer types in the SAME namespace: distinct hint
    //    names, both generate (no overwrite). --

    [Fact]
    public void SameNamedValidators_NestedInDifferentOuterTypes_BothGenerate()
    {
        const string consumer = """
            using Moongazing.OrionGuard.DependencyInjection;
            namespace Sample
            {
                public sealed class Customer { public string Name { get; set; } = ""; }

                public partial class Outer1
                {
                    [Moongazing.OrionGuard.OpenApi.OpenApiValidator("customer.json", "#/components/schemas/Customer")]
                    public partial class InnerValidator : IValidator<Customer> { }
                }

                public partial class Outer2
                {
                    [Moongazing.OrionGuard.OpenApi.OpenApiValidator("customer.json", "#/components/schemas/Customer")]
                    public partial class InnerValidator : IValidator<Customer> { }
                }
            }
            """;

        var run = GeneratorTestHarness.Run(consumer, "customer.json", Document);

        // Two generated sources, not one overwriting the other: the hint name carries the full path so
        // Sample.Outer1.InnerValidator and Sample.Outer2.InnerValidator key distinctly.
        Assert.Equal(2, run.GeneratedSources.Count(s => s.Contains("partial class InnerValidator")));

        var assembly = GeneratorTestHarness.Compile(consumer, "customer.json", Document);
        Assert.NotNull(assembly.GetType("Sample.Outer1+InnerValidator"));
        Assert.NotNull(assembly.GetType("Sample.Outer2+InnerValidator"));
    }

    // -- Nested under a NON-partial enclosing class: OG1011, nothing generated (a partial reopening a
    //    non-partial outer type would be a consumer compile error). --

    [Fact]
    public void ValidatorNestedUnderNonPartialOuter_ReportsOG1011_AndGeneratesNothing()
    {
        const string consumer = """
            using Moongazing.OrionGuard.DependencyInjection;
            namespace Sample
            {
                public sealed class Customer { public string Name { get; set; } = ""; }

                public class Outer
                {
                    [Moongazing.OrionGuard.OpenApi.OpenApiValidator("customer.json", "#/components/schemas/Customer")]
                    public partial class CustomerValidator : IValidator<Customer> { }
                }
            }
            """;

        var run = GeneratorTestHarness.Run(consumer, "customer.json", Document);

        Assert.True(HasDiagnostic(run, "OG1011"));

        // The non-partial outer type is the named offender, and no validator source was added at all:
        // assert the skip directly via the validator-source count, not by probing the concatenated text.
        Assert.Contains(
            run.GeneratorDiagnostics,
            d => d.Id == "OG1011" && d.GetMessage().Contains("Outer"));
        Assert.Equal(0, run.ValidatorSourceCount);
    }

    // -- Nested NON-partial target under a partial outer: OG1011 names the target, nothing generated. --

    [Fact]
    public void NestedNonPartialTarget_ReportsOG1011_AndGeneratesNothing()
    {
        const string consumer = """
            using Moongazing.OrionGuard.DependencyInjection;
            namespace Sample
            {
                public sealed class Customer { public string Name { get; set; } = ""; }

                public partial class Outer
                {
                    [Moongazing.OrionGuard.OpenApi.OpenApiValidator("customer.json", "#/components/schemas/Customer")]
                    public class CustomerValidator : IValidator<Customer> { }
                }
            }
            """;

        var run = GeneratorTestHarness.Run(consumer, "customer.json", Document);

        Assert.True(HasDiagnostic(run, "OG1011"));
        Assert.Contains(
            run.GeneratorDiagnostics,
            d => d.Id == "OG1011" && d.GetMessage().Contains("CustomerValidator"));
        Assert.Equal(0, run.ValidatorSourceCount);
    }

    // -- Enclosing 'partial record' keyword is reproduced (a 'partial class' over a record would not
    //    compile), and the nested validator compiles. --

    [Fact]
    public void ValidatorNestedUnderPartialRecord_ReproducesRecordKeyword_AndCompiles()
    {
        const string consumer = """
            using Moongazing.OrionGuard.DependencyInjection;
            namespace Sample
            {
                public sealed class Customer { public string Name { get; set; } = ""; }

                public partial record Outer
                {
                    [Moongazing.OrionGuard.OpenApi.OpenApiValidator("customer.json", "#/components/schemas/Customer")]
                    public partial class CustomerValidator : IValidator<Customer> { }
                }
            }
            """;

        var run = GeneratorTestHarness.Run(consumer, "customer.json", Document);

        // The enclosing keyword must be 'record', not 'class': the emitter must reopen the user's record as
        // a partial record or the two declarations disagree and the consumer build fails.
        Assert.Contains("partial record Outer", run.AllGeneratedText);
        Assert.DoesNotContain("partial class Outer", run.AllGeneratedText);
        Assert.Equal(1, run.ValidatorSourceCount);

        var assembly = GeneratorTestHarness.Compile(consumer, "customer.json", Document);
        Assert.NotNull(assembly.GetType("Sample.Outer+CustomerValidator"));
    }

    // -- Top-level target in the GLOBAL namespace (no namespace): still generates and compiles. --

    [Fact]
    public void TopLevelTargetInGlobalNamespace_GeneratesAndCompiles()
    {
        const string consumer = """
            using Moongazing.OrionGuard.DependencyInjection;

            public sealed class Customer { public string Name { get; set; } = ""; }

            [Moongazing.OrionGuard.OpenApi.OpenApiValidator("customer.json", "#/components/schemas/Customer")]
            public partial class CustomerValidator : IValidator<Customer> { }
            """;

        var run = GeneratorTestHarness.Run(consumer, "customer.json", Document);
        Assert.Contains("partial class CustomerValidator", run.AllGeneratedText);

        var assembly = GeneratorTestHarness.Compile(consumer, "customer.json", Document);
        var validatorType = assembly.GetType("CustomerValidator");
        Assert.NotNull(validatorType);

        var customerType = assembly.GetType("Customer")!;
        dynamic instance = Activator.CreateInstance(customerType)!;
        instance.Name = "ab"; // below minLength 3

        var result = GeneratorTestHarness.Validate(assembly, "CustomerValidator", (object)instance);
        Assert.Contains(result.Errors, e => e.ParameterName == "name" && e.ErrorCode == "MIN_LENGTH");
    }

    // -- Generic target: diagnosed with OG1010, generates nothing (no broken code). --

    [Fact]
    public void GenericTarget_ReportsOG1010_AndGeneratesNothing()
    {
        const string consumer = """
            using Moongazing.OrionGuard.DependencyInjection;
            namespace Sample
            {
                public sealed class Customer { public string Name { get; set; } = ""; }

                [Moongazing.OrionGuard.OpenApi.OpenApiValidator("customer.json", "#/components/schemas/Customer")]
                public partial class CustomerValidator<T> : IValidator<Customer> { }
            }
            """;

        var run = GeneratorTestHarness.Run(consumer, "customer.json", Document);

        Assert.True(HasDiagnostic(run, "OG1010"));
        Assert.Equal(0, run.ValidatorSourceCount);
    }

    // -- Target nested inside a generic type: also diagnosed with OG1010, generates nothing. --

    [Fact]
    public void TargetNestedInsideGenericType_ReportsOG1010_AndGeneratesNothing()
    {
        const string consumer = """
            using Moongazing.OrionGuard.DependencyInjection;
            namespace Sample
            {
                public sealed class Customer { public string Name { get; set; } = ""; }

                public partial class Outer<T>
                {
                    [Moongazing.OrionGuard.OpenApi.OpenApiValidator("customer.json", "#/components/schemas/Customer")]
                    public partial class CustomerValidator : IValidator<Customer> { }
                }
            }
            """;

        var run = GeneratorTestHarness.Run(consumer, "customer.json", Document);

        Assert.True(HasDiagnostic(run, "OG1010"));

        // The generator emitted no validator source of its own (only the always-injected marker attribute):
        // assert that directly via the validator-source count, so the check cannot pass for an unrelated
        // reason the way an "absent substring" check could.
        Assert.Equal(0, run.ValidatorSourceCount);
    }

    // -- Enclosing 'partial interface' keyword is reproduced (C# allows nested types in interfaces; a
    //    'partial class' over an interface would not compile), and the nested validator compiles. --

    [Fact]
    public void ValidatorNestedUnderPartialInterface_ReproducesInterfaceKeyword_AndCompiles()
    {
        const string consumer = """
            using Moongazing.OrionGuard.DependencyInjection;
            namespace Sample
            {
                public sealed class Customer { public string Name { get; set; } = ""; }

                public partial interface IOuter
                {
                    [Moongazing.OrionGuard.OpenApi.OpenApiValidator("customer.json", "#/components/schemas/Customer")]
                    public partial class CustomerValidator : IValidator<Customer> { }
                }
            }
            """;

        var run = GeneratorTestHarness.Run(consumer, "customer.json", Document);

        // The enclosing keyword must be 'interface', not 'class': reopening the user's interface as a
        // 'partial class' (or any non-interface keyword) is a consumer compile error.
        Assert.Contains("partial interface IOuter", run.AllGeneratedText);
        Assert.DoesNotContain("partial class IOuter", run.AllGeneratedText);
        Assert.Equal(1, run.ValidatorSourceCount);

        var assembly = GeneratorTestHarness.Compile(consumer, "customer.json", Document);
        Assert.NotNull(assembly.GetType("Sample.IOuter+CustomerValidator"));
    }

    // -- Enclosing 'partial record struct' keyword is reproduced (a record struct can contain nested types;
    //    a 'partial struct' or 'partial class' over it would not compile), and the nested validator compiles. --

    [Fact]
    public void ValidatorNestedUnderPartialRecordStruct_ReproducesRecordStructKeyword_AndCompiles()
    {
        const string consumer = """
            using Moongazing.OrionGuard.DependencyInjection;
            namespace Sample
            {
                public sealed class Customer { public string Name { get; set; } = ""; }

                public partial record struct Outer
                {
                    [Moongazing.OrionGuard.OpenApi.OpenApiValidator("customer.json", "#/components/schemas/Customer")]
                    public partial class CustomerValidator : IValidator<Customer> { }
                }
            }
            """;

        var run = GeneratorTestHarness.Run(consumer, "customer.json", Document);

        // The enclosing keyword must be 'record struct'. Reopening as a bare 'struct', 'record', or 'class'
        // disagrees with the user's declaration and fails the consumer build.
        Assert.Contains("partial record struct Outer", run.AllGeneratedText);
        Assert.Equal(1, run.ValidatorSourceCount);

        var assembly = GeneratorTestHarness.Compile(consumer, "customer.json", Document);
        Assert.NotNull(assembly.GetType("Sample.Outer+CustomerValidator"));
    }

    // -- Deeply nested 3-level chain mixing class / record / interface keywords: every link reopened with
    //    its own keyword, and the validator compiles at the bottom of the chain. --

    [Fact]
    public void ValidatorNestedThreeLevels_MixedKinds_ReproducesEveryKeyword_AndCompiles()
    {
        const string consumer = """
            using Moongazing.OrionGuard.DependencyInjection;
            namespace Sample
            {
                public sealed class Customer { public string Name { get; set; } = ""; }

                public partial class Outer
                {
                    public partial record Middle
                    {
                        public partial interface IInner
                        {
                            [Moongazing.OrionGuard.OpenApi.OpenApiValidator("customer.json", "#/components/schemas/Customer")]
                            public partial class CustomerValidator : IValidator<Customer> { }
                        }
                    }
                }
            }
            """;

        var run = GeneratorTestHarness.Run(consumer, "customer.json", Document);

        // Each of the three enclosing links is reopened with its own keyword; a single wrong keyword
        // anywhere in the chain would fail the consumer build.
        Assert.Contains("partial class Outer", run.AllGeneratedText);
        Assert.Contains("partial record Middle", run.AllGeneratedText);
        Assert.Contains("partial interface IInner", run.AllGeneratedText);
        Assert.Equal(1, run.ValidatorSourceCount);

        var assembly = GeneratorTestHarness.Compile(consumer, "customer.json", Document);
        Assert.NotNull(assembly.GetType("Sample.Outer+Middle+IInner+CustomerValidator"));
    }

    // -- A 3-level chain where a MIDDLE link is non-partial: OG1011 names that link, nothing generated.
    //    (Guards a chain that mixes partial and non-partial enclosing types.) --

    [Fact]
    public void ValidatorNestedThreeLevels_MiddleLinkNonPartial_ReportsOG1011_AndGeneratesNothing()
    {
        const string consumer = """
            using Moongazing.OrionGuard.DependencyInjection;
            namespace Sample
            {
                public sealed class Customer { public string Name { get; set; } = ""; }

                public partial class Outer
                {
                    public class Middle
                    {
                        [Moongazing.OrionGuard.OpenApi.OpenApiValidator("customer.json", "#/components/schemas/Customer")]
                        public partial class CustomerValidator : IValidator<Customer> { }
                    }
                }
            }
            """;

        var run = GeneratorTestHarness.Run(consumer, "customer.json", Document);

        // Middle is the first non-partial link in the chain and must be named; nothing is generated.
        Assert.Contains(
            run.GeneratorDiagnostics,
            d => d.Id == "OG1011" && d.GetMessage().Contains("Middle"));
        Assert.Equal(0, run.ValidatorSourceCount);
    }

    // -- Target nested directly under a partial type in the GLOBAL namespace (no namespace): the enclosing
    //    type is still reopened and the validator compiles. --

    [Fact]
    public void ValidatorNestedInGlobalNamespace_ReopensEnclosing_AndCompiles()
    {
        const string consumer = """
            using Moongazing.OrionGuard.DependencyInjection;

            public sealed class Customer { public string Name { get; set; } = ""; }

            public partial class Outer
            {
                [Moongazing.OrionGuard.OpenApi.OpenApiValidator("customer.json", "#/components/schemas/Customer")]
                public partial class CustomerValidator : IValidator<Customer> { }
            }
            """;

        var run = GeneratorTestHarness.Run(consumer, "customer.json", Document);
        Assert.Contains("partial class Outer", run.AllGeneratedText);
        Assert.Equal(1, run.ValidatorSourceCount);

        var assembly = GeneratorTestHarness.Compile(consumer, "customer.json", Document);
        Assert.NotNull(assembly.GetType("Outer+CustomerValidator"));
    }
}
