using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Moongazing.OrionGuard.Compatibility;
using Moongazing.OrionGuard.Migration;

namespace Moongazing.OrionGuard.Migration.Tests;

/// <summary>
/// The strongest correctness proof: migrate a corpus of FluentValidation validators, then compile
/// the migrated output against the REAL OrionGuard assembly. If the codemod emitted a method that
/// does not exist on the compatibility builder, this compilation fails. The model type the
/// validators target is supplied alongside so member access also type-checks.
/// </summary>
public sealed class MigratedOutputCompilationTests
{
    private const string ModelDeclarations =
        """
        namespace Sample
        {
            public sealed class Model
            {
                public string Name { get; set; } = string.Empty;
                public string Email { get; set; } = string.Empty;
                public string? Bio { get; set; }
                public string Code { get; set; } = string.Empty;
                public int Age { get; set; }
                public decimal Total { get; set; }
                public string Country { get; set; } = string.Empty;
            }
        }
        """;

    public static TheoryData<string> SupportedValidators() => new()
    {
        // Every supported rule exercised across a few representative validators.
        """
        using FluentValidation;
        namespace Sample
        {
            public class V1 : AbstractValidator<Model>
            {
                public V1()
                {
                    RuleFor(x => x.Name).NotNull().NotEmpty().MinimumLength(2).MaximumLength(100);
                    RuleFor(x => x.Email).NotEmpty().EmailAddress();
                    RuleFor(x => x.Code).ExactLength(6);
                    RuleFor(x => x.Name).Length(2, 100).Matches("^[a-z]+$");
                }
            }
        }
        """,
        """
        using FluentValidation;
        namespace Sample
        {
            public class V2 : AbstractValidator<Model>
            {
                public V2()
                {
                    RuleFor(x => x.Age).GreaterThan(0).LessThan(130);
                    RuleFor(x => x.Age).GreaterThanOrEqualTo(18).LessThanOrEqualTo(120);
                    RuleFor(x => x.Age).InclusiveBetween(0, 120);
                    RuleFor(x => x.Age).ExclusiveBetween(0, 130);
                }
            }
        }
        """,
        """
        using FluentValidation;
        namespace Sample
        {
            public class V3 : AbstractValidator<Model>
            {
                public V3()
                {
                    RuleFor(x => x.Country).Equal("TR").WithMessage("must be TR");
                    RuleFor(x => x.Name).NotEqual("admin").WithErrorCode("RESERVED");
                    RuleFor(x => x.Bio).MaximumLength(500).When(x => x.Bio != null);
                    RuleFor(x => x.Name).NotEmpty().Unless(x => x.Age == 0);
                    RuleFor(x => x.Age).Must(age => age % 2 == 0);
                }
            }
        }
        """,
    };

    [Theory]
    [MemberData(nameof(SupportedValidators))]
    public void MigratedSupportedValidator_Compiles(string fluentValidationSource)
    {
        var migrated = MigrationEngine.Migrate("/repo/V.cs", fluentValidationSource);

        Assert.False(migrated.HasUnmigrated);
        AssertCompiles(migrated.MigratedText);
    }

    [Fact]
    public void MigratedValidator_WithNoFluentValidationUsing_AddsCompatibilityUsingAndCompiles()
    {
        // The base type is fully qualified and there is NO `using FluentValidation;` to swap. The
        // rewriter must still ensure the OrionGuard compatibility namespace is imported, otherwise
        // the renamed (unqualified) base type FluentStyleValidator<Model> would not resolve.
        const string source =
            """
            namespace Sample
            {
                public class NoUsingValidator : FluentValidation.AbstractValidator<Model>
                {
                    public NoUsingValidator()
                    {
                        RuleFor(x => x.Name).NotEmpty().MaximumLength(50);
                    }
                }
            }
            """;

        var migrated = MigrationEngine.Migrate("/repo/NoUsingValidator.cs", source);

        Assert.Contains(
            "using Moongazing.OrionGuard.Compatibility;",
            migrated.MigratedText,
            StringComparison.Ordinal);
        Assert.False(migrated.HasUnmigrated);
        AssertCompiles(migrated.MigratedText);
    }

    [Fact]
    public void MigratedValidator_WithGlobalUsing_AddsCompatibilityUsingAndCompiles()
    {
        // FluentValidation is imported elsewhere (a global using), so this file carries no
        // `using FluentValidation;` directive to swap. The base type AbstractValidator<Model> is
        // therefore unqualified, yet the rewriter must still add the compatibility using so the
        // renamed base resolves. The migrated file is self-contained once that using is present, so
        // it compiles on its own without the original FluentValidation reference.
        const string validator =
            """
            namespace Sample
            {
                public class GlobalUsingValidator : AbstractValidator<Model>
                {
                    public GlobalUsingValidator()
                    {
                        RuleFor(x => x.Email).NotEmpty().EmailAddress();
                    }
                }
            }
            """;

        var migrated = MigrationEngine.Migrate("/repo/GlobalUsingValidator.cs", validator);

        Assert.Contains(
            "using Moongazing.OrionGuard.Compatibility;",
            migrated.MigratedText,
            StringComparison.Ordinal);
        Assert.False(migrated.HasUnmigrated);
        AssertCompiles(migrated.MigratedText);
    }

    [Fact]
    public void MigratedValidator_ActuallyValidates_WhenCompiledAndRun()
    {
        // Beyond compiling, confirm the migrated validator behaves: a blank Name fails NotEmpty.
        const string source =
            """
            using FluentValidation;
            namespace Sample
            {
                public class RunnableValidator : AbstractValidator<Model>
                {
                    public RunnableValidator()
                    {
                        RuleFor(x => x.Name).NotEmpty().MaximumLength(10);
                        RuleFor(x => x.Email).NotEmpty().EmailAddress();
                    }
                }
            }
            """;

        var migrated = MigrationEngine.Migrate("/repo/RunnableValidator.cs", source);
        var assembly = EmitAssembly(migrated.MigratedText);

        var validatorType = assembly.GetType("Sample.RunnableValidator")!;
        var modelType = assembly.GetType("Sample.Model")!;
        var validator = Activator.CreateInstance(validatorType)!;

        var invalidModel = Activator.CreateInstance(modelType)!; // empty Name, empty Email
        var validateMethod = validatorType.GetMethod("Validate", new[] { modelType })!;
        var result = validateMethod.Invoke(validator, new[] { invalidModel })!;

        var isInvalid = (bool)result.GetType().GetProperty("IsInvalid")!.GetValue(result)!;
        Assert.True(isInvalid);
    }

    private static void AssertCompiles(string migratedValidatorSource)
    {
        var compilation = BuildCompilation(migratedValidatorSource);
        var diagnostics = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        Assert.True(
            diagnostics.Count == 0,
            "Migrated output failed to compile against the real OrionGuard API:\n" +
            string.Join("\n", diagnostics.Select(d => d.ToString())));
    }

    private static Assembly EmitAssembly(string migratedValidatorSource)
    {
        var compilation = BuildCompilation(migratedValidatorSource);
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);

        Assert.True(
            emit.Success,
            string.Join("\n", emit.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString())));

        stream.Seek(0, SeekOrigin.Begin);
        return Assembly.Load(stream.ToArray());
    }

    private static CSharpCompilation BuildCompilation(string migratedValidatorSource)
    {
        var trees = new[]
        {
            CSharpSyntaxTree.ParseText(migratedValidatorSource),
            CSharpSyntaxTree.ParseText(ModelDeclarations),
        };

        var references = ReferenceAssemblies()
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList();

        return CSharpCompilation.Create(
            "MigratedValidators",
            trees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static IEnumerable<string> ReferenceAssemblies()
    {
        // Pull the full set of trusted-platform assemblies so System.* references resolve, plus the
        // real OrionGuard assembly that defines FluentStyleValidator / FluentRuleBuilder.
        var trusted = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;
        foreach (var path in trusted.Split(Path.PathSeparator))
        {
            if (!string.IsNullOrEmpty(path))
            {
                yield return path;
            }
        }

        yield return typeof(FluentStyleValidator<>).Assembly.Location;
    }
}
