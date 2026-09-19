namespace Moongazing.OrionGuard.Generators.Tests;

using static GeneratorTestHarness;

/// <summary>
/// Target shapes <c>[GenerateValidator]</c> must handle: same-named types in different namespaces,
/// records, inherited properties, nested types, and non-public types.
/// </summary>
public class GenerateValidatorTargetShapeTests
{
    [Fact]
    public void Generator_ShouldGenerateBothValidators_WhenTwoNamespacesDeclareTheSameTypeName()
    {
        // Both hint names used to be "CreateRequestValidator.g.cs"; Roslyn rejects the duplicate
        // (CS8785) and drops every source the generator produced.
        const string source = """
            using Moongazing.OrionGuard.Attributes;
            using Moongazing.OrionGuard.Generators;

            namespace Orders { [GenerateValidator] public class CreateRequest { [NotNull] public string? Name { get; set; } } }
            namespace Users { [GenerateValidator] public class CreateRequest { [NotNull] public string? Name { get; set; } } }
            """;

        var assembly = Compile(source, new OrionGuardGenerator());

        foreach (var ns in new[] { "Orders", "Users" })
        {
            var result = Validate(assembly, ns + ".CreateRequestValidator", New(assembly, ns + ".CreateRequest"));
            Assert.Contains(result.Errors, e => e.ParameterName == "Name" && e.ErrorCode == "NOT_NULL");
        }
    }

    [Fact]
    public void Generator_ShouldGenerateValidators_ForRecordTargets()
    {
        const string source = """
            using Moongazing.OrionGuard.Attributes;
            using Moongazing.OrionGuard.Generators;

            namespace App
            {
                [GenerateValidator] public record Signup { [NotNull] public string? Email { get; init; } }
                [GenerateValidator] public record class Invite { [NotNull] public string? Code { get; init; } }
                [GenerateValidator] public record struct Slot { [Positive] public int Minutes { get; init; } }
                [GenerateValidator] public record Contact([property: NotNull] string? Phone);
            }
            """;

        var assembly = Compile(source, new OrionGuardGenerator());

        var contact = Activator.CreateInstance(assembly.GetType("App.Contact")!, new object?[] { null })!;
        Assert.Contains(Validate(assembly, "App.ContactValidator", contact).Errors,
            e => e.ParameterName == "Phone" && e.ErrorCode == "NOT_NULL");
        Assert.Contains(Validate(assembly, "App.SignupValidator", New(assembly, "App.Signup")).Errors,
            e => e.ParameterName == "Email" && e.ErrorCode == "NOT_NULL");
        Assert.Contains(Validate(assembly, "App.InviteValidator", New(assembly, "App.Invite")).Errors,
            e => e.ParameterName == "Code" && e.ErrorCode == "NOT_NULL");
        Assert.Contains(Validate(assembly, "App.SlotValidator", New(assembly, "App.Slot")).Errors,
            e => e.ParameterName == "Minutes" && e.ErrorCode == "POSITIVE");
    }

    [Fact]
    public void GeneratedValidator_ShouldCheckInheritedProperties()
    {
        const string source = """
            using Moongazing.OrionGuard.Attributes;
            using Moongazing.OrionGuard.Generators;

            namespace App
            {
                public class Base
                {
                    [NotNull] public string? Name { get; set; }
                    [NotNull] public virtual string? Code { get; set; }
                }

                [GenerateValidator]
                public class Derived : Base
                {
                    [Positive] public int Age { get; set; } = 5;

                    // An override without attributes keeps the base declaration's rules.
                    public override string? Code { get; set; }
                }
            }
            """;

        var assembly = Compile(source, new OrionGuardGenerator());

        var result = Validate(assembly, "App.DerivedValidator", New(assembly, "App.Derived"));

        Assert.Contains(result.Errors, e => e.ParameterName == "Name" && e.ErrorCode == "NOT_NULL");
        Assert.Contains(result.Errors, e => e.ParameterName == "Code" && e.ErrorCode == "NOT_NULL");
        Assert.DoesNotContain(result.Errors, e => e.ParameterName == "Age");
    }

    [Fact]
    public void GeneratedValidator_ShouldCompile_ForNestedAndInternalTypes()
    {
        // The validator lives at namespace level and referred to a nested type by its simple name
        // (CS0246); a public validator over an internal type is inconsistent accessibility (CS0051).
        const string source = """
            using Moongazing.OrionGuard.Attributes;
            using Moongazing.OrionGuard.Generators;

            namespace App
            {
                public class Outer
                {
                    [GenerateValidator] public class Inner { [NotNull] public string? Name { get; set; } }
                }

                [GenerateValidator] internal sealed class Hidden { [NotNull] public string? Name { get; set; } }
            }

            [GenerateValidator] public sealed class GlobalRequest { [NotNull] public string? Name { get; set; } }
            """;

        var assembly = Compile(source, new OrionGuardGenerator());

        Assert.Contains(Validate(assembly, "App.InnerValidator", New(assembly, "App.Outer+Inner")).Errors,
            e => e.ParameterName == "Name" && e.ErrorCode == "NOT_NULL");
        Assert.Contains(Validate(assembly, "App.HiddenValidator", New(assembly, "App.Hidden")).Errors,
            e => e.ParameterName == "Name" && e.ErrorCode == "NOT_NULL");
        Assert.Contains(Validate(assembly, "GlobalRequestValidator", New(assembly, "GlobalRequest")).Errors,
            e => e.ParameterName == "Name" && e.ErrorCode == "NOT_NULL");
        Assert.False(assembly.GetType("App.HiddenValidator")!.IsPublic);
    }
    [Fact]
    public void GeneratedValidator_ShouldAllocateItsErrorList_OnlyOnTheFirstFailure()
    {
        // Validation that passes is the common case; it used to allocate a list it never filled.
        const string source = """
            using Moongazing.OrionGuard.Attributes;
            using Moongazing.OrionGuard.Generators;

            namespace App
            {
                [GenerateValidator]
                public sealed class Sample
                {
                    [NotNull] public string? Name { get; set; }
                }
            }
            """;

        var (_, run) = Run(source, new OrionGuardGenerator());
        var generated = run.Results.Single().GeneratedSources
            .Single(s => s.HintName.Contains("SampleValidator", StringComparison.Ordinal))
            .SourceText.ToString();

        Assert.DoesNotContain("var errors = new System.Collections.Generic.List", generated, StringComparison.Ordinal);
        Assert.Contains("errors ??= new System.Collections.Generic.List", generated, StringComparison.Ordinal);

        var assembly = Compile(source, new OrionGuardGenerator());
        Assert.True(Validate(assembly, "App.SampleValidator", New(assembly, "App.Sample", ("Name", "Ada"))).IsValid);
        Assert.Contains(Validate(assembly, "App.SampleValidator", New(assembly, "App.Sample")).Errors,
            e => e.ParameterName == "Name");
    }
}
