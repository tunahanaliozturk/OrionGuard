using System.Reflection;
using Moongazing.OrionGuard.Core;

namespace Moongazing.OrionGuard.Migration.Tests;

/// <summary>
/// BUG-C5: a rule the codemod migrates must give the verdict FluentValidation gives, not only compile. The corpus
/// holds one FluentValidation validator per mapped rule; it is migrated, compiled against the real OrionGuard
/// assembly and run. Every expected verdict is FluentValidation's, taken from its validators: NotEmptyValidator,
/// LengthValidator, the comparison and range validators (a null value passes, a null threshold fails), and
/// PredicateValidator (a null value reaches the predicate).
/// </summary>
public sealed class MappedRuleSemanticsTests
{
    // The model's defaults satisfy every validator, so each case changes one property and nothing else fails.
    private const string Corpus =
        """
        #nullable enable
        using System;
        using System.Collections.Generic;
        using FluentValidation;

        namespace Semantics
        {
            public sealed class Model
            {
                public string? Text { get; set; } = "abc";
                public List<int>? Items { get; set; } = new List<int> { 1 };
                public int Count { get; set; } = 1;
                public int? MaybeCount { get; set; } = 1;
                public decimal Price { get; set; } = 1m;
                public decimal? MaybePrice { get; set; } = 1m;
                public double Ratio { get; set; } = 1d;
                public long Big { get; set; } = 1L;
                public Guid Id { get; set; } = new Guid("6f9619ff-8b86-d011-b42d-00c04fc964ff");
                public DateTime Moment { get; set; } = new DateTime(2020, 1, 1);
                public bool Flag { get; set; } = true;
            }

            public class NotNullRule : AbstractValidator<Model>
            {
                public NotNullRule()
                {
                    RuleFor(x => x.Text).NotNull();
                    RuleFor(x => x.MaybeCount).NotNull();
                }
            }

            public class NotEmptyRule : AbstractValidator<Model>
            {
                public NotEmptyRule()
                {
                    RuleFor(x => x.Text).NotEmpty();
                    RuleFor(x => x.Items).NotEmpty();
                    RuleFor(x => x.Count).NotEmpty();
                    RuleFor(x => x.MaybeCount).NotEmpty();
                    RuleFor(x => x.Price).NotEmpty();
                    RuleFor(x => x.Id).NotEmpty();
                    RuleFor(x => x.Moment).NotEmpty();
                    RuleFor(x => x.Flag).NotEmpty();
                }
            }

            public class EqualRule : AbstractValidator<Model>
            {
                public EqualRule()
                {
                    RuleFor(x => x.Text).Equal("abc");
                    RuleFor(x => x.Price).Equal(1);
                }
            }

            public class NotEqualRule : AbstractValidator<Model>
            {
                public NotEqualRule()
                {
                    RuleFor(x => x.Text).NotEqual("admin");
                    RuleFor(x => x.Price).NotEqual(0);
                }
            }

            public class LengthRule : AbstractValidator<Model>
            {
                public LengthRule()
                {
                    RuleFor(x => x.Text).Length(2, 5);
                }
            }

            public class ExactLengthRule : AbstractValidator<Model>
            {
                public ExactLengthRule()
                {
                    RuleFor(x => x.Text).Length(3);
                }
            }

            public class MinimumLengthRule : AbstractValidator<Model>
            {
                public MinimumLengthRule()
                {
                    RuleFor(x => x.Text).MinimumLength(2);
                }
            }

            public class MaximumLengthRule : AbstractValidator<Model>
            {
                public MaximumLengthRule()
                {
                    RuleFor(x => x.Text).MaximumLength(5);
                }
            }

            public class GreaterThanRule : AbstractValidator<Model>
            {
                public GreaterThanRule()
                {
                    RuleFor(x => x.Count).GreaterThan(0);
                    RuleFor(x => x.Price).GreaterThan(0);
                    RuleFor(x => x.MaybePrice).GreaterThan(0);
                    RuleFor(x => x.Ratio).GreaterThan(0);
                    RuleFor(x => x.Big).GreaterThan(0);
                    RuleFor(x => x.Moment).GreaterThan(new DateTime(2000, 1, 1));
                }
            }

            public class GreaterThanOrEqualToRule : AbstractValidator<Model>
            {
                public GreaterThanOrEqualToRule()
                {
                    RuleFor(x => x.Price).GreaterThanOrEqualTo(0);
                    RuleFor(x => x.Count).GreaterThanOrEqualTo(1);
                }
            }

            public class LessThanRule : AbstractValidator<Model>
            {
                public LessThanRule()
                {
                    RuleFor(x => x.Price).LessThan(100);
                    RuleFor(x => x.Big).LessThan(10);
                }
            }

            public class LessThanOrEqualToRule : AbstractValidator<Model>
            {
                public LessThanOrEqualToRule()
                {
                    RuleFor(x => x.Price).LessThanOrEqualTo(100);
                }
            }

            public class InclusiveBetweenRule : AbstractValidator<Model>
            {
                public InclusiveBetweenRule()
                {
                    RuleFor(x => x.Price).InclusiveBetween(1, 10);
                    RuleFor(x => x.MaybePrice).InclusiveBetween(1, 10);
                }
            }

            public class ExclusiveBetweenRule : AbstractValidator<Model>
            {
                public ExclusiveBetweenRule()
                {
                    RuleFor(x => x.Price).ExclusiveBetween(0, 10);
                }
            }

            public class MustRule : AbstractValidator<Model>
            {
                public MustRule()
                {
                    RuleFor(x => x.Count).Must(count => count % 2 == 1);
                    RuleFor(x => x.Text).Must(text => text != null);
                }
            }

            public class WithMessageRule : AbstractValidator<Model>
            {
                public WithMessageRule()
                {
                    RuleFor(x => x.Text).Length(2, 5).WithMessage("Text must be 2 to 5 characters.");
                    RuleFor(x => x.Text).NotEmpty().MaximumLength(5).WithMessage("Text is too long.");
                }
            }

            public class WithErrorCodeRule : AbstractValidator<Model>
            {
                public WithErrorCodeRule()
                {
                    RuleFor(x => x.Text).Length(2, 5).WithErrorCode("TEXT_LENGTH");
                }
            }

            public class WhenRule : AbstractValidator<Model>
            {
                public WhenRule()
                {
                    RuleFor(x => x.Text).NotEmpty().MaximumLength(3).When(x => x.Flag);
                }
            }

            public class UnlessRule : AbstractValidator<Model>
            {
                public UnlessRule()
                {
                    RuleFor(x => x.Text).NotEmpty().Unless(x => x.Flag);
                }
            }
        }
        """;

    private static readonly Lazy<Assembly> Migrated = new(() =>
        MigratedOutputCompilationTests.EmitAssembly(MigrationEngine.Migrate("/repo/Semantics.cs", Corpus).MigratedText));

    [Fact]
    public void Corpus_IsMigratedWithoutFindings()
    {
        var result = MigrationEngine.Migrate("/repo/Semantics.cs", Corpus);

        Assert.False(result.HasUnmigrated, string.Join("\n", result.Findings.Select(f => $"{f.Rule}: {f.Reason}")));
    }

    [Fact]
    public void NotNull_KeepsFluentValidationVerdicts()
    {
        AssertVerdict("NotNullRule", "Text", null, fluentValidationIsValid: false);
        AssertVerdict("NotNullRule", "Text", "", fluentValidationIsValid: true);
        AssertVerdict("NotNullRule", "MaybeCount", null, fluentValidationIsValid: false);
        AssertVerdict("NotNullRule", "MaybeCount", 0, fluentValidationIsValid: true);
    }

    [Fact]
    public void NotEmpty_KeepsFluentValidationVerdicts()
    {
        AssertVerdict("NotEmptyRule", "Text", null, fluentValidationIsValid: false);
        AssertVerdict("NotEmptyRule", "Text", "", fluentValidationIsValid: false);
        AssertVerdict("NotEmptyRule", "Text", "  ", fluentValidationIsValid: false);
        AssertVerdict("NotEmptyRule", "Items", new List<int>(), fluentValidationIsValid: false);
        AssertVerdict("NotEmptyRule", "Count", 0, fluentValidationIsValid: false);
        AssertVerdict("NotEmptyRule", "Count", -1, fluentValidationIsValid: true);
        AssertVerdict("NotEmptyRule", "MaybeCount", null, fluentValidationIsValid: false);
        // default(int?) is null, so FluentValidation accepts a nullable holding 0.
        AssertVerdict("NotEmptyRule", "MaybeCount", 0, fluentValidationIsValid: true);
        AssertVerdict("NotEmptyRule", "Price", 0m, fluentValidationIsValid: false);
        AssertVerdict("NotEmptyRule", "Id", Guid.Empty, fluentValidationIsValid: false);
        AssertVerdict("NotEmptyRule", "Moment", DateTime.MinValue, fluentValidationIsValid: false);
        AssertVerdict("NotEmptyRule", "Flag", false, fluentValidationIsValid: false);
    }

    [Fact]
    public void Equal_KeepsFluentValidationVerdicts()
    {
        AssertVerdict("EqualRule", "Text", "ABC", fluentValidationIsValid: false);
        AssertVerdict("EqualRule", "Text", null, fluentValidationIsValid: false);
        AssertVerdict("EqualRule", "Price", 1.00m, fluentValidationIsValid: true);
        AssertVerdict("EqualRule", "Price", 2m, fluentValidationIsValid: false);
    }

    [Fact]
    public void NotEqual_KeepsFluentValidationVerdicts()
    {
        AssertVerdict("NotEqualRule", "Text", "admin", fluentValidationIsValid: false);
        AssertVerdict("NotEqualRule", "Text", "Admin", fluentValidationIsValid: true);
        AssertVerdict("NotEqualRule", "Text", null, fluentValidationIsValid: true);
        AssertVerdict("NotEqualRule", "Price", 0.00m, fluentValidationIsValid: false);
    }

    [Fact]
    public void Length_KeepsFluentValidationVerdicts()
    {
        AssertVerdict("LengthRule", "Text", "a", fluentValidationIsValid: false);
        AssertVerdict("LengthRule", "Text", "", fluentValidationIsValid: false);
        AssertVerdict("LengthRule", "Text", "ab", fluentValidationIsValid: true);
        AssertVerdict("LengthRule", "Text", "abcde", fluentValidationIsValid: true);
        AssertVerdict("LengthRule", "Text", "abcdef", fluentValidationIsValid: false);
        AssertVerdict("LengthRule", "Text", null, fluentValidationIsValid: true);
    }

    [Fact]
    public void ExactLength_KeepsFluentValidationVerdicts()
    {
        AssertVerdict("ExactLengthRule", "Text", "ab", fluentValidationIsValid: false);
        AssertVerdict("ExactLengthRule", "Text", "abc", fluentValidationIsValid: true);
        AssertVerdict("ExactLengthRule", "Text", "abcd", fluentValidationIsValid: false);
        AssertVerdict("ExactLengthRule", "Text", null, fluentValidationIsValid: true);
    }

    [Fact]
    public void MinimumLength_KeepsFluentValidationVerdicts()
    {
        AssertVerdict("MinimumLengthRule", "Text", "a", fluentValidationIsValid: false);
        AssertVerdict("MinimumLengthRule", "Text", "", fluentValidationIsValid: false);
        AssertVerdict("MinimumLengthRule", "Text", "ab", fluentValidationIsValid: true);
        AssertVerdict("MinimumLengthRule", "Text", null, fluentValidationIsValid: true);
    }

    [Fact]
    public void MaximumLength_KeepsFluentValidationVerdicts()
    {
        AssertVerdict("MaximumLengthRule", "Text", "abcdef", fluentValidationIsValid: false);
        AssertVerdict("MaximumLengthRule", "Text", "abcde", fluentValidationIsValid: true);
        AssertVerdict("MaximumLengthRule", "Text", "", fluentValidationIsValid: true);
        AssertVerdict("MaximumLengthRule", "Text", null, fluentValidationIsValid: true);
    }

    [Fact]
    public void GreaterThan_KeepsFluentValidationVerdicts()
    {
        AssertVerdict("GreaterThanRule", "Count", 0, fluentValidationIsValid: false);
        AssertVerdict("GreaterThanRule", "Price", 0m, fluentValidationIsValid: false);
        AssertVerdict("GreaterThanRule", "Price", -5m, fluentValidationIsValid: false);
        AssertVerdict("GreaterThanRule", "Price", 0.01m, fluentValidationIsValid: true);
        AssertVerdict("GreaterThanRule", "MaybePrice", null, fluentValidationIsValid: true);
        AssertVerdict("GreaterThanRule", "MaybePrice", 0m, fluentValidationIsValid: false);
        AssertVerdict("GreaterThanRule", "Ratio", 0d, fluentValidationIsValid: false);
        AssertVerdict("GreaterThanRule", "Ratio", 0.5d, fluentValidationIsValid: true);
        AssertVerdict("GreaterThanRule", "Big", 0L, fluentValidationIsValid: false);
        AssertVerdict("GreaterThanRule", "Big", long.MaxValue, fluentValidationIsValid: true);
        AssertVerdict("GreaterThanRule", "Moment", new DateTime(1999, 1, 1), fluentValidationIsValid: false);
    }

    [Fact]
    public void GreaterThanOrEqualTo_KeepsFluentValidationVerdicts()
    {
        AssertVerdict("GreaterThanOrEqualToRule", "Price", -0.01m, fluentValidationIsValid: false);
        AssertVerdict("GreaterThanOrEqualToRule", "Price", 0m, fluentValidationIsValid: true);
        AssertVerdict("GreaterThanOrEqualToRule", "Count", 0, fluentValidationIsValid: false);
    }

    [Fact]
    public void LessThan_KeepsFluentValidationVerdicts()
    {
        AssertVerdict("LessThanRule", "Price", 100m, fluentValidationIsValid: false);
        AssertVerdict("LessThanRule", "Price", 99.99m, fluentValidationIsValid: true);
        AssertVerdict("LessThanRule", "Big", 10L, fluentValidationIsValid: false);
    }

    [Fact]
    public void LessThanOrEqualTo_KeepsFluentValidationVerdicts()
    {
        AssertVerdict("LessThanOrEqualToRule", "Price", 100m, fluentValidationIsValid: true);
        AssertVerdict("LessThanOrEqualToRule", "Price", 100.01m, fluentValidationIsValid: false);
    }

    [Fact]
    public void InclusiveBetween_KeepsFluentValidationVerdicts()
    {
        AssertVerdict("InclusiveBetweenRule", "Price", 0.99m, fluentValidationIsValid: false);
        AssertVerdict("InclusiveBetweenRule", "Price", 10m, fluentValidationIsValid: true);
        AssertVerdict("InclusiveBetweenRule", "Price", 10.01m, fluentValidationIsValid: false);
        AssertVerdict("InclusiveBetweenRule", "MaybePrice", null, fluentValidationIsValid: true);
        AssertVerdict("InclusiveBetweenRule", "MaybePrice", 11m, fluentValidationIsValid: false);
    }

    [Fact]
    public void ExclusiveBetween_KeepsFluentValidationVerdicts()
    {
        AssertVerdict("ExclusiveBetweenRule", "Price", 0m, fluentValidationIsValid: false);
        AssertVerdict("ExclusiveBetweenRule", "Price", 0.01m, fluentValidationIsValid: true);
        AssertVerdict("ExclusiveBetweenRule", "Price", 10m, fluentValidationIsValid: false);
    }

    [Fact]
    public void Must_KeepsFluentValidationVerdicts()
    {
        AssertVerdict("MustRule", "Count", 2, fluentValidationIsValid: false);
        AssertVerdict("MustRule", "Count", 3, fluentValidationIsValid: true);
        AssertVerdict("MustRule", "Text", null, fluentValidationIsValid: false);
    }

    [Fact]
    public void WithMessage_ReplacesTheMessageOfTheLastRuleOnly()
    {
        // Length is one validator in FluentValidation, so its message covers both bounds.
        Assert.Equal(new[] { "Text must be 2 to 5 characters." }, Messages("WithMessageRule", "Text", "a"));
        Assert.Equal(
            new[] { "Text must be 2 to 5 characters.", "Text is too long." },
            Messages("WithMessageRule", "Text", "abcdef"));
        // An empty value fails NotEmpty, which keeps its default message, and the Length rule.
        var emptyMessages = Messages("WithMessageRule", "Text", "");
        Assert.Contains("Text must be 2 to 5 characters.", emptyMessages);
        Assert.DoesNotContain("Text is too long.", emptyMessages);
    }

    [Fact]
    public void WithErrorCode_ReplacesTheCodeForBothLengthBounds()
    {
        Assert.Equal("TEXT_LENGTH", Assert.Single(Run("WithErrorCodeRule", ("Text", "a")).Errors).ErrorCode);
        Assert.Equal("TEXT_LENGTH", Assert.Single(Run("WithErrorCodeRule", ("Text", "abcdef")).Errors).ErrorCode);
    }

    [Fact]
    public void When_AppliesToEveryPrecedingRule()
    {
        AssertVerdict("WhenRule", "Text", "", fluentValidationIsValid: false);
        AssertVerdict("WhenRule", "Text", "abcd", fluentValidationIsValid: false);
        AssertVerdict("WhenRule", fluentValidationIsValid: true, ("Flag", false), ("Text", ""));
        AssertVerdict("WhenRule", fluentValidationIsValid: true, ("Flag", false), ("Text", "abcd"));
    }

    [Fact]
    public void Unless_AppliesToEveryPrecedingRule()
    {
        AssertVerdict("UnlessRule", "Text", "", fluentValidationIsValid: true);
        AssertVerdict("UnlessRule", fluentValidationIsValid: false, ("Flag", false), ("Text", ""));
    }

    private static void AssertVerdict(string validator, string property, object? value, bool fluentValidationIsValid) =>
        AssertVerdict(validator, fluentValidationIsValid, (property, value));

    private static void AssertVerdict(
        string validator,
        bool fluentValidationIsValid,
        params (string Property, object? Value)[] assignments)
    {
        var result = Run(validator, assignments);

        Assert.True(
            result.IsValid == fluentValidationIsValid,
            $"{validator} with {string.Join(", ", assignments.Select(a => $"{a.Property} = {a.Value ?? "null"}"))}: " +
            $"FluentValidation says {(fluentValidationIsValid ? "valid" : "invalid")}, the migrated validator " +
            $"reported [{string.Join("; ", result.Errors.Select(e => e.Message))}].");
    }

    private static string[] Messages(string validator, string property, object? value) =>
        Run(validator, (property, value)).Errors.Select(e => e.Message).ToArray();

    private static GuardResult Run(string validator, params (string Property, object? Value)[] assignments)
    {
        var assembly = Migrated.Value;
        var modelType = assembly.GetType("Semantics.Model", throwOnError: true)!;
        var validatorType = assembly.GetType("Semantics." + validator, throwOnError: true)!;

        var model = Activator.CreateInstance(modelType)!;
        foreach (var (property, value) in assignments)
        {
            modelType.GetProperty(property)!.SetValue(model, value);
        }

        var instance = Activator.CreateInstance(validatorType)!;
        var validate = validatorType.GetMethod("Validate", new[] { modelType })!;
        return (GuardResult)validate.Invoke(instance, new[] { model })!;
    }
}
