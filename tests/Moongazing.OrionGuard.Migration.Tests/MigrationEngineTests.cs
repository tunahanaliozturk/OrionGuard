using Moongazing.OrionGuard.Migration;

namespace Moongazing.OrionGuard.Migration.Tests;

public sealed class MigrationEngineTests
{
    private const string Path = "/repo/Validators/SampleValidator.cs";

    private static string Wrap(string ruleBody) =>
        $$"""
        using FluentValidation;

        namespace Sample;

        public class SampleValidator : AbstractValidator<Model>
        {
            public SampleValidator()
            {
        {{ruleBody}}
            }
        }
        """;

    [Fact]
    public void Migrate_RewritesUsingDirective()
    {
        var result = MigrationEngine.Migrate(Path, Wrap("        RuleFor(x => x.Name).NotEmpty();"));

        Assert.Contains("using Moongazing.OrionGuard.Compatibility;", result.MigratedText, StringComparison.Ordinal);
        Assert.DoesNotContain("using FluentValidation;", result.MigratedText, StringComparison.Ordinal);
    }

    [Fact]
    public void Migrate_RewritesBaseType_PreservingTypeArgument()
    {
        var result = MigrationEngine.Migrate(Path, Wrap("        RuleFor(x => x.Name).NotEmpty();"));

        Assert.Contains(": FluentStyleValidator<Model>", result.MigratedText, StringComparison.Ordinal);
        Assert.DoesNotContain("AbstractValidator", result.MigratedText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("RuleFor(x => x.Name).NotEmpty().MaximumLength(100);", "NotEmpty().MaximumLength(100)")]
    [InlineData("RuleFor(x => x.Email).NotEmpty().EmailAddress();", "NotEmpty().EmailAddress()")]
    [InlineData("RuleFor(x => x.Age).InclusiveBetween(18, 120);", "InclusiveBetween(18, 120)")]
    [InlineData("RuleFor(x => x.Age).GreaterThanOrEqualTo(18);", "GreaterThanOrEqualTo(18)")]
    [InlineData("RuleFor(x => x.Name).MinimumLength(3);", "MinimumLength(3)")]
    [InlineData("RuleFor(x => x.Name).Equal(\"a\");", "Equal(\"a\")")]
    [InlineData("RuleFor(x => x.Name).NotEqual(\"a\");", "NotEqual(\"a\")")]
    [InlineData("RuleFor(x => x.Name).Matches(\"^a$\");", "Matches(\"^a$\")")]
    public void Migrate_SupportedChain_IsPreservedVerbatim(string rule, string expectedFragment)
    {
        var result = MigrationEngine.Migrate(Path, Wrap("        " + rule));

        Assert.Contains(expectedFragment, result.MigratedText, StringComparison.Ordinal);
        Assert.False(result.HasUnmigrated);
    }

    [Fact]
    public void Migrate_ExactLength_BecomesLengthWithEqualBounds()
    {
        var result = MigrationEngine.Migrate(Path, Wrap("        RuleFor(x => x.Code).ExactLength(6);"));

        Assert.Contains("Length(6, 6)", result.MigratedText, StringComparison.Ordinal);
        Assert.DoesNotContain("ExactLength", result.MigratedText, StringComparison.Ordinal);
    }

    [Fact]
    public void Migrate_ConditionalWhen_IsPreserved()
    {
        var result = MigrationEngine.Migrate(
            Path, Wrap("        RuleFor(x => x.Bio).MaximumLength(500).When(x => x.Bio != null);"));

        Assert.Contains("MaximumLength(500).When(x => x.Bio != null)", result.MigratedText, StringComparison.Ordinal);
        Assert.False(result.HasUnmigrated);
    }

    [Fact]
    public void Migrate_CustomMessage_IsPreserved()
    {
        var result = MigrationEngine.Migrate(
            Path, Wrap("        RuleFor(x => x.Name).NotEmpty().WithMessage(\"required\");"));

        Assert.Contains("NotEmpty().WithMessage(\"required\")", result.MigratedText, StringComparison.Ordinal);
        Assert.False(result.HasUnmigrated);
    }

    [Fact]
    public void Migrate_UnsupportedRule_LeavesChainUntouchedWithTodoAndReports()
    {
        var result = MigrationEngine.Migrate(
            Path, Wrap("        RuleFor(x => x.Total).ScalePrecision(2, 10);"));

        Assert.Contains("// TODO: OrionGuard migration - ScalePrecision", result.MigratedText, StringComparison.Ordinal);
        Assert.Contains("ScalePrecision(2, 10)", result.MigratedText, StringComparison.Ordinal);
        Assert.True(result.HasUnmigrated);
        var finding = Assert.Single(result.Findings);
        Assert.Equal("ScalePrecision", finding.Rule);
        Assert.Equal(Path, finding.FilePath);
    }

    [Fact]
    public void Migrate_SetValidator_IsReportedNotTranslated()
    {
        var result = MigrationEngine.Migrate(
            Path, Wrap("        RuleFor(x => x.Child).SetValidator(new ChildValidator());"));

        Assert.Contains("// TODO: OrionGuard migration - SetValidator", result.MigratedText, StringComparison.Ordinal);
        Assert.Contains("SetValidator(new ChildValidator())", result.MigratedText, StringComparison.Ordinal);
        Assert.Single(result.Findings);
    }

    [Fact]
    public void Migrate_MultipleRuleForOnDifferentProperties_AllSupported()
    {
        var body =
            "        RuleFor(x => x.Name).NotEmpty().MaximumLength(50);\n" +
            "        RuleFor(x => x.Email).NotEmpty().EmailAddress();\n" +
            "        RuleFor(x => x.Age).InclusiveBetween(0, 120);";

        var result = MigrationEngine.Migrate(Path, Wrap(body));

        Assert.False(result.HasUnmigrated);
        Assert.Contains("MaximumLength(50)", result.MigratedText, StringComparison.Ordinal);
        Assert.Contains("EmailAddress()", result.MigratedText, StringComparison.Ordinal);
        Assert.Contains("InclusiveBetween(0, 120)", result.MigratedText, StringComparison.Ordinal);
    }

    [Fact]
    public void Migrate_MixedSupportedAndUnsupported_IsPartialWithReport()
    {
        var body =
            "        RuleFor(x => x.Name).NotEmpty().MaximumLength(50);\n" +
            "        RuleFor(x => x.Total).ScalePrecision(2, 10);\n" +
            "        RuleFor(x => x.Child).SetValidator(new ChildValidator());";

        var result = MigrationEngine.Migrate(Path, Wrap(body));

        // Supported chain migrated.
        Assert.Contains("MaximumLength(50)", result.MigratedText, StringComparison.Ordinal);
        // Unsupported chains left untouched with markers.
        Assert.Contains("// TODO: OrionGuard migration - ScalePrecision", result.MigratedText, StringComparison.Ordinal);
        Assert.Contains("// TODO: OrionGuard migration - SetValidator", result.MigratedText, StringComparison.Ordinal);
        Assert.Equal(2, result.Findings.Count);
    }

    [Fact]
    public void Migrate_ChainWithOneUnsupportedRule_LeavesWholeChainUntouched()
    {
        // A single chain mixing a supported and an unsupported rule must NOT be partially rewritten.
        var result = MigrationEngine.Migrate(
            Path, Wrap("        RuleFor(x => x.Name).NotEmpty().Empty();"));

        Assert.Contains("RuleFor(x => x.Name).NotEmpty().Empty();", result.MigratedText, StringComparison.Ordinal);
        Assert.Contains("// TODO: OrionGuard migration - Empty", result.MigratedText, StringComparison.Ordinal);
        Assert.Single(result.Findings);
    }

    [Fact]
    public void Migrate_RuleForEach_IsReportedNotSilentlySkipped()
    {
        var result = MigrationEngine.Migrate(
            Path, Wrap("        RuleForEach(x => x.Tags).NotEmpty();"));

        Assert.Contains("// TODO: OrionGuard migration - RuleForEach", result.MigratedText, StringComparison.Ordinal);
        Assert.Contains("RuleForEach(x => x.Tags).NotEmpty();", result.MigratedText, StringComparison.Ordinal);
        var finding = Assert.Single(result.Findings);
        Assert.Equal("RuleForEach", finding.Rule);
        Assert.Equal(Path, finding.FilePath);
    }

    [Fact]
    public void Migrate_Include_IsReportedNotSilentlySkipped()
    {
        var result = MigrationEngine.Migrate(
            Path, Wrap("        Include(new BaseValidator());"));

        Assert.Contains("// TODO: OrionGuard migration - Include", result.MigratedText, StringComparison.Ordinal);
        Assert.Contains("Include(new BaseValidator());", result.MigratedText, StringComparison.Ordinal);
        var finding = Assert.Single(result.Findings);
        Assert.Equal("Include", finding.Rule);
    }

    [Fact]
    public void Migrate_UnsupportedOverloadOfSupportedRule_IsReportedAndLeftUntouched()
    {
        // GreaterThan has a member-comparison (lambda) overload with the same arity as the
        // constant-threshold form the compatibility builder supports. It must be reported and the
        // chain left byte-for-byte untouched, NOT mistranslated onto GreaterThan(IComparable).
        var result = MigrationEngine.Migrate(
            Path, Wrap("        RuleFor(x => x.Age).GreaterThan(x => x.MinAge);"));

        Assert.Contains("// TODO: OrionGuard migration - GreaterThan", result.MigratedText, StringComparison.Ordinal);
        Assert.Contains("RuleFor(x => x.Age).GreaterThan(x => x.MinAge);", result.MigratedText, StringComparison.Ordinal);
        var finding = Assert.Single(result.Findings);
        Assert.Equal("GreaterThan", finding.Rule);
    }

    [Fact]
    public void Migrate_EqualWithMemberLambdaOverload_IsReportedNotMistranslated()
    {
        var result = MigrationEngine.Migrate(
            Path, Wrap("        RuleFor(x => x.Name).Equal(x => x.Code);"));

        Assert.Contains("// TODO: OrionGuard migration - Equal", result.MigratedText, StringComparison.Ordinal);
        Assert.Contains("RuleFor(x => x.Name).Equal(x => x.Code);", result.MigratedText, StringComparison.Ordinal);
        Assert.Single(result.Findings);
    }

    [Fact]
    public void Migrate_NonValidatorFile_IsUnchanged()
    {
        const string source =
            """
            namespace Sample;

            public class NotAValidator
            {
                public int Value { get; set; }
            }
            """;

        var result = MigrationEngine.Migrate(Path, source);

        Assert.False(result.HasChanges);
        Assert.False(result.HasUnmigrated);
        Assert.Equal(source, result.MigratedText);
    }

    [Fact]
    public void Migrate_TypeDerivingFromNonFluentValidationAbstractValidator_IsNotMigrated()
    {
        // A bare AbstractValidator<T> that is NOT FluentValidation's: the file imports a different
        // library, and there is no `using FluentValidation;`. The codemod must leave it untouched
        // rather than rewrite an unrelated base type (the over-broad-matching regression).
        const string source =
            """
            using SomeOtherLib;

            namespace Sample;

            public class SampleValidator : AbstractValidator<Model>
            {
                public SampleValidator()
                {
                    RuleFor(x => x.Name).NotEmpty();
                }
            }
            """;

        var result = MigrationEngine.Migrate(Path, source);

        Assert.False(result.HasChanges);
        Assert.False(result.HasUnmigrated);
        Assert.Equal(source, result.MigratedText);
    }

    [Fact]
    public void Migrate_FullyQualifiedNonFluentValidationAbstractValidator_IsNotMigrated()
    {
        // A fully qualified base whose qualifier is NOT FluentValidation must not match.
        const string source =
            """
            namespace Sample;

            public class SampleValidator : SomeOtherLib.AbstractValidator<Model>
            {
                public SampleValidator()
                {
                    RuleFor(x => x.Name).NotEmpty();
                }
            }
            """;

        var result = MigrationEngine.Migrate(Path, source);

        Assert.False(result.HasChanges);
        Assert.Equal(source, result.MigratedText);
    }

    [Fact]
    public void Migrate_UnrelatedIncludeCall_IsNotReportedAsFluentValidationConstruct()
    {
        // An EF-Core-style `.Include(...)` inside a genuine validator (for example in a Must body)
        // is a member access on another receiver, not a FluentValidation Include(...). It must NOT
        // be reported (the Include over-reporting regression).
        const string source =
            """
            using FluentValidation;

            namespace Sample;

            public class SampleValidator : AbstractValidator<Model>
            {
                public SampleValidator()
                {
                    var query = GetQuery();
                    query.Include(x => x.Orders).ToList();
                    RuleFor(x => x.Name).NotEmpty();
                }

                private static System.Linq.IQueryable<Model> GetQuery() => null!;
            }
            """;

        var result = MigrationEngine.Migrate(Path, source);

        // The supported RuleFor chain still migrates, but the .Include(...) is left alone and unreported.
        Assert.False(result.HasUnmigrated);
        Assert.Contains("query.Include(x => x.Orders).ToList();", result.MigratedText, StringComparison.Ordinal);
        Assert.DoesNotContain("TODO: OrionGuard migration", result.MigratedText, StringComparison.Ordinal);
    }

    [Fact]
    public void Migrate_IncludeOutsideValidator_IsNotReported()
    {
        // A bare Include(...) in an ordinary (non-validator) class must never be reported.
        const string source =
            """
            namespace Sample;

            public class Repository
            {
                public void Load()
                {
                    Include(new object());
                }

                private static void Include(object o) { }
            }
            """;

        var result = MigrationEngine.Migrate(Path, source);

        Assert.False(result.HasChanges);
        Assert.False(result.HasUnmigrated);
        Assert.Equal(source, result.MigratedText);
    }

    [Fact]
    public void Migrate_ExactLengthWithNonTrivialArgument_DuplicatesExpressionCorrectly()
    {
        // ExactLength(expr) -> Length(expr, expr) must clone the WHOLE argument expression, not just
        // a trivial literal, into a fresh node for the second argument.
        var result = MigrationEngine.Migrate(
            Path, Wrap("        RuleFor(x => x.Code).ExactLength(MaxLength + 1);"));

        Assert.Contains("Length(MaxLength + 1, MaxLength + 1)", result.MigratedText, StringComparison.Ordinal);
        Assert.DoesNotContain("ExactLength", result.MigratedText, StringComparison.Ordinal);
        Assert.False(result.HasUnmigrated);
    }

    [Fact]
    public void Migrate_IsIdempotent_DoesNotStackTodoMarkers()
    {
        var first = MigrationEngine.Migrate(
            Path, Wrap("        RuleFor(x => x.Total).ScalePrecision(2, 10);"));
        var second = MigrationEngine.Migrate(Path, first.MigratedText);

        var occurrences = second.MigratedText.Split("TODO: OrionGuard migration").Length - 1;
        Assert.Equal(1, occurrences);
    }
}
