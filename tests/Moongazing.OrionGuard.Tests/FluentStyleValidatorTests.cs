using System.Linq.Expressions;
using System.Reflection;
using Moongazing.OrionGuard.Compatibility;
using Moongazing.OrionGuard.Core;

namespace Moongazing.OrionGuard.Tests;

/// <summary>
/// BUG-C5: the FluentValidation compatibility builder changed validation semantics. <c>NotEmpty</c> passed every
/// non-string (an empty list, <c>0</c>, <see cref="Guid.Empty"/>), comparisons threw
/// "Object must be of type Decimal" for <c>GreaterThan(0)</c> on a decimal, and <c>Length(min, max)</c> was two
/// rules, so <c>WithMessage</c> only covered the maximum. PERF-2: every validator instance recompiled its
/// property accessors.
/// </summary>
public class FluentStyleValidatorTests
{
    private sealed class Model
    {
        public string? Name { get; set; } = "Ada";
        public List<int>? Items { get; set; } = new() { 1 };
        public IEnumerable<int>? Sequence { get; set; } = new[] { 1 };
        public int Count { get; set; } = 1;
        public int? MaybeCount { get; set; } = 1;
        public decimal Price { get; set; } = 1m;
        public decimal? MaybePrice { get; set; } = 1m;
        public long Big { get; set; } = 1L;
        public double Ratio { get; set; } = 1d;
        public Guid Id { get; set; } = Guid.NewGuid();
        public DateTime Moment { get; set; } = new(2020, 1, 1);
        public bool Flag { get; set; } = true;
    }

    /// <summary>One <c>RuleFor</c> on one property, configured by the test.</summary>
    private sealed class RuleValidator<TProperty> : FluentStyleValidator<Model>
    {
        public RuleValidator(
            Expression<Func<Model, TProperty?>> selector,
            Action<FluentRuleBuilder<Model, TProperty>> configure)
        {
            configure(RuleFor(selector));
        }
    }

    private sealed class NameValidator : FluentStyleValidator<Model>
    {
        public NameValidator()
        {
            NameRule = RuleFor(m => m.Name).NotEmpty();
        }

        public FluentRuleBuilder<Model, string> NameRule { get; }
    }

    #region NotEmpty

    [Fact]
    public void NotEmpty_ShouldFail_WhenCollectionOrSequenceIsEmpty()
    {
        Assert.True(ValidateRule(m => m.Items, b => b.NotEmpty(), m => m.Items = new()).IsInvalid);
        // A lazy query is not an ICollection, so this goes through the enumerator check.
        Assert.True(ValidateRule(m => m.Sequence, b => b.NotEmpty(), m => m.Sequence = Enumerable.Range(1, 3).Where(i => i > 5)).IsInvalid);
    }

    [Fact]
    public void NotEmpty_ShouldFail_WhenValueIsTheDefaultOfItsType()
    {
        Assert.True(ValidateRule(m => m.Count, b => b.NotEmpty(), m => m.Count = 0).IsInvalid);
        Assert.True(ValidateRule(m => m.Price, b => b.NotEmpty(), m => m.Price = 0m).IsInvalid);
        Assert.True(ValidateRule(m => m.Id, b => b.NotEmpty(), m => m.Id = Guid.Empty).IsInvalid);
        Assert.True(ValidateRule(m => m.Moment, b => b.NotEmpty(), m => m.Moment = DateTime.MinValue).IsInvalid);
        Assert.True(ValidateRule(m => m.Flag, b => b.NotEmpty(), m => m.Flag = false).IsInvalid);
    }

    [Fact]
    public void NotEmpty_ShouldFail_WhenValueIsNullOrWhitespace()
    {
        Assert.True(ValidateRule(m => m.Name, b => b.NotEmpty(), m => m.Name = null).IsInvalid);
        Assert.True(ValidateRule(m => m.Name, b => b.NotEmpty(), m => m.Name = " \t").IsInvalid);
        Assert.True(ValidateRule(m => m.MaybeCount, b => b.NotEmpty(), m => m.MaybeCount = null).IsInvalid);
    }

    [Fact]
    public void NotEmpty_ShouldPass_WhenValueIsSet()
    {
        Assert.True(ValidateRule(m => m.Items, b => b.NotEmpty(), _ => { }).IsValid);
        Assert.True(ValidateRule(m => m.Sequence, b => b.NotEmpty(), m => m.Sequence = Enumerable.Range(1, 3).Where(i => i > 2)).IsValid);
        Assert.True(ValidateRule(m => m.Count, b => b.NotEmpty(), m => m.Count = -1).IsValid);
        Assert.True(ValidateRule(m => m.Id, b => b.NotEmpty(), _ => { }).IsValid);
        // FluentValidation compares with default(int?), which is null, so a nullable holding 0 is not empty.
        Assert.True(ValidateRule(m => m.MaybeCount, b => b.NotEmpty(), m => m.MaybeCount = 0).IsValid);
    }

    [Fact]
    public void NotNull_ShouldPass_WhenValueIsTheDefaultOfItsType()
    {
        Assert.True(ValidateRule(m => m.Count, b => b.NotNull(), m => m.Count = 0).IsValid);
        Assert.True(ValidateRule(m => m.Items, b => b.NotNull(), m => m.Items = new()).IsValid);
        Assert.True(ValidateRule(m => m.Name, b => b.NotNull(), m => m.Name = null).IsInvalid);
    }

    #endregion

    #region Comparisons

    [Theory]
    [InlineData("GreaterThan", -1, false)]
    [InlineData("GreaterThan", 0, false)]
    [InlineData("GreaterThan", 0.5, true)]
    [InlineData("GreaterThanOrEqualTo", -0.5, false)]
    [InlineData("GreaterThanOrEqualTo", 0, true)]
    [InlineData("LessThan", 10, false)]
    [InlineData("LessThan", 9.5, true)]
    [InlineData("LessThanOrEqualTo", 10.5, false)]
    [InlineData("LessThanOrEqualTo", 10, true)]
    [InlineData("InclusiveBetween", -0.5, false)]
    [InlineData("InclusiveBetween", 0, true)]
    [InlineData("InclusiveBetween", 10, true)]
    [InlineData("InclusiveBetween", 10.5, false)]
    [InlineData("ExclusiveBetween", 0, false)]
    [InlineData("ExclusiveBetween", 0.5, true)]
    [InlineData("ExclusiveBetween", 10, false)]
    public void ComparisonRule_ShouldCompareDecimalByValue_WhenThresholdIsAnIntLiteral(string rule, double price, bool expectedValid)
    {
        var result = ValidateRule(m => m.Price, b => ApplyComparison(b, rule), m => m.Price = (decimal)price);

        Assert.Equal(expectedValid, result.IsValid);
    }

    [Fact]
    public void GreaterThan_ShouldCompareByValue_WhenPropertyIsLongOrDouble()
    {
        Assert.True(ValidateRule(m => m.Big, b => b.GreaterThan(0), m => m.Big = -1L).IsInvalid);
        Assert.True(ValidateRule(m => m.Big, b => b.GreaterThan(0), m => m.Big = long.MaxValue).IsValid);
        Assert.True(ValidateRule(m => m.Ratio, b => b.GreaterThan(0), m => m.Ratio = -0.5).IsInvalid);
        Assert.True(ValidateRule(m => m.Ratio, b => b.GreaterThan(0), m => m.Ratio = 0.5).IsValid);
    }

    [Fact]
    public void GreaterThan_ShouldSkipNullAndCompareValue_WhenPropertyIsNullableDecimal()
    {
        Assert.True(ValidateRule(m => m.MaybePrice, b => b.GreaterThan(0), m => m.MaybePrice = null).IsValid);
        Assert.True(ValidateRule(m => m.MaybePrice, b => b.GreaterThan(0), m => m.MaybePrice = -1m).IsInvalid);
        Assert.True(ValidateRule(m => m.MaybePrice, b => b.GreaterThan(0), m => m.MaybePrice = 1m).IsValid);
    }

    [Fact]
    public void GreaterThan_ShouldUseTheTypesOwnOrdering_WhenValueIsNotNumeric()
    {
        var threshold = new DateTime(2000, 1, 1);

        Assert.True(ValidateRule(m => m.Moment, b => b.GreaterThan(threshold), m => m.Moment = new DateTime(2020, 1, 1)).IsValid);
        Assert.True(ValidateRule(m => m.Moment, b => b.GreaterThan(threshold), m => m.Moment = new DateTime(1999, 1, 1)).IsInvalid);
    }

    [Fact]
    public void ComparisonRules_ShouldFailInsteadOfThrowing_WhenValueAndThresholdCannotBeCompared()
    {
        Assert.True(ValidateRule(m => m.Moment, b => b.GreaterThan(0), _ => { }).IsInvalid);
        Assert.True(ValidateRule(m => m.Name, b => b.LessThan(10), _ => { }).IsInvalid);
        Assert.True(ValidateRule(m => m.Price, b => b.InclusiveBetween("a", "z"), _ => { }).IsInvalid);
        // FluentValidation fails a comparison against a null threshold.
        Assert.True(ValidateRule(m => m.Price, b => b.GreaterThan(null!), _ => { }).IsInvalid);
    }

    [Fact]
    public void ComparisonRules_ShouldFail_WhenValueIsNaN()
    {
        Assert.True(ValidateRule(m => m.Ratio, b => b.GreaterThan(0d), m => m.Ratio = double.NaN).IsInvalid);
        Assert.True(ValidateRule(m => m.Ratio, b => b.LessThan(10d), m => m.Ratio = double.NaN).IsInvalid);
    }

    [Fact]
    public void Equal_ShouldCompareByValue_WhenDecimalPropertyIsComparedWithAnIntLiteral()
    {
        // Equal takes TProperty, so the compiler converts the literal; no cross-type comparison happens.
        Assert.True(ValidateRule(m => m.Price, b => b.Equal(1), m => m.Price = 1.00m).IsValid);
        Assert.True(ValidateRule(m => m.Price, b => b.NotEqual(0), m => m.Price = 0.00m).IsInvalid);
    }

    #endregion

    #region Length

    [Fact]
    public void Length_ShouldApplyWithMessageToBothBounds()
    {
        const string message = "Name must be 2 to 5 characters.";

        var tooShort = ValidateRule(m => m.Name, b => b.Length(2, 5).WithMessage(message), m => m.Name = "a");
        var tooLong = ValidateRule(m => m.Name, b => b.Length(2, 5).WithMessage(message), m => m.Name = "abcdef");

        Assert.Equal(message, Assert.Single(tooShort.Errors).Message);
        Assert.Equal(message, Assert.Single(tooLong.Errors).Message);
    }

    [Fact]
    public void Length_ShouldApplyWithErrorCodeToBothBounds()
    {
        var tooShort = ValidateRule(m => m.Name, b => b.Length(2, 5).WithErrorCode("NAME_LENGTH"), m => m.Name = "a");
        var tooLong = ValidateRule(m => m.Name, b => b.Length(2, 5).WithErrorCode("NAME_LENGTH"), m => m.Name = "abcdef");

        Assert.Equal("NAME_LENGTH", Assert.Single(tooShort.Errors).ErrorCode);
        Assert.Equal("NAME_LENGTH", Assert.Single(tooLong.Errors).ErrorCode);
    }

    [Fact]
    public void Length_ShouldKeepTheBoundSpecificCodes_WhenNothingIsOverridden()
    {
        Assert.Equal("MIN_LENGTH", Assert.Single(ValidateRule(m => m.Name, b => b.Length(2, 5), m => m.Name = "a").Errors).ErrorCode);
        Assert.Equal("MAX_LENGTH", Assert.Single(ValidateRule(m => m.Name, b => b.Length(2, 5), m => m.Name = "abcdef").Errors).ErrorCode);
        Assert.True(ValidateRule(m => m.Name, b => b.Length(2, 5), m => m.Name = null).IsValid);
    }

    #endregion

    #region Accessor cache

    [Fact]
    public void RuleFor_ShouldReuseTheCompiledAccessor_WhenAnotherValidatorInstanceIsCreated()
    {
        var first = new NameValidator();
        var second = new NameValidator();

        Assert.Same(CompiledAccessor(first.NameRule), CompiledAccessor(second.NameRule));
        Assert.True(second.Validate(new Model { Name = "" }).IsInvalid);
    }

    #endregion

    private static GuardResult ValidateRule<TProperty>(
        Expression<Func<Model, TProperty?>> selector,
        Action<FluentRuleBuilder<Model, TProperty>> configure,
        Action<Model> arrange)
    {
        var model = new Model();
        arrange(model);
        return new RuleValidator<TProperty>(selector, configure).Validate(model);
    }

    private static FluentRuleBuilder<Model, TProperty> ApplyComparison<TProperty>(FluentRuleBuilder<Model, TProperty> builder, string rule) =>
        rule switch
        {
            "GreaterThan" => builder.GreaterThan(0),
            "GreaterThanOrEqualTo" => builder.GreaterThanOrEqualTo(0),
            "LessThan" => builder.LessThan(10),
            "LessThanOrEqualTo" => builder.LessThanOrEqualTo(10),
            "InclusiveBetween" => builder.InclusiveBetween(0, 10),
            "ExclusiveBetween" => builder.ExclusiveBetween(0, 10),
            _ => throw new ArgumentOutOfRangeException(nameof(rule), rule, "Unknown comparison rule."),
        };

    /// <summary>
    /// The core assembly exposes no internals to this project, and the accessor is private; reading the field is
    /// the only way to observe that the second validator did not compile its own delegate.
    /// </summary>
    private static object? CompiledAccessor<TProperty>(FluentRuleBuilder<Model, TProperty> builder) =>
        typeof(FluentRuleBuilder<Model, TProperty>)
            .GetField("_accessor", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(builder);
}
