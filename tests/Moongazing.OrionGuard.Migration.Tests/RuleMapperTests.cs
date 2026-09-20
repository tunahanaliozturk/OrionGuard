using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Moongazing.OrionGuard.Migration.Tests;

public sealed class RuleMapperTests
{
    private static ArgumentListSyntax Args(params string[] argumentExpressions)
    {
        var inner = string.Join(", ", argumentExpressions);
        var invocation = (InvocationExpressionSyntax)SyntaxFactory.ParseExpression($"M({inner})");
        return invocation.ArgumentList;
    }

    [Theory]
    [InlineData("NotNull", 0, "NotNull")]
    [InlineData("NotEmpty", 0, "NotEmpty")]
    [InlineData("Equal", 1, "Equal")]
    [InlineData("NotEqual", 1, "NotEqual")]
    [InlineData("Length", 2, "Length")]
    [InlineData("MinimumLength", 1, "MinimumLength")]
    [InlineData("MaximumLength", 1, "MaximumLength")]
    [InlineData("GreaterThan", 1, "GreaterThan")]
    [InlineData("GreaterThanOrEqualTo", 1, "GreaterThanOrEqualTo")]
    [InlineData("LessThan", 1, "LessThan")]
    [InlineData("LessThanOrEqualTo", 1, "LessThanOrEqualTo")]
    [InlineData("InclusiveBetween", 2, "InclusiveBetween")]
    [InlineData("ExclusiveBetween", 2, "ExclusiveBetween")]
    [InlineData("WithErrorCode", 1, "WithErrorCode")]
    public void Map_SupportedRule_ReturnsTargetMethod(string rule, int argCount, string expectedTarget)
    {
        // Non-lambda constant arguments exercise the value-shaped overloads these rules support.
        var args = Args(Enumerable.Repeat("0", argCount).ToArray());

        var mapping = RuleMapper.Map(rule, args);

        Assert.True(mapping.IsSupported);
        Assert.Equal(expectedTarget, mapping.TargetMethod);
        Assert.Equal(ArgumentTransform.Verbatim, mapping.ArgumentTransform);
    }

    [Fact]
    public void Map_LengthWithOneArgument_MapsToLengthWithArgumentDuplication()
    {
        // FluentValidation's exact-length rule is Length(n).
        var mapping = RuleMapper.Map("Length", Args("6"));

        Assert.True(mapping.IsSupported);
        Assert.Equal("Length", mapping.TargetMethod);
        Assert.Equal(ArgumentTransform.DuplicateSingleArgument, mapping.ArgumentTransform);
    }

    [Theory]
    [InlineData("x => x.Min", "x => x.Max")]
    [InlineData("x => x.Exact")]
    // A method group is the Func<T, int> overload, and duplicating a call would evaluate it twice.
    [InlineData("GetLimit")]
    [InlineData("GetLimit()")]
    [InlineData("Limits.Exact")]
    public void Map_LengthWithLambdaBounds_IsReported(params string[] arguments)
    {
        // Length(Func<T, int>, Func<T, int>) and Length(Func<T, int>) would not compile against Length(int, int).
        var mapping = RuleMapper.Map("Length", Args(arguments));

        Assert.False(mapping.IsSupported);
        Assert.Contains("Length", mapping.UnsupportedReason, StringComparison.Ordinal);
    }

    [Fact]
    public void Map_ExactLength_IsReportedAsNotAFluentValidationRule()
    {
        // FluentValidation has no ExactLength rule, so an ExactLength call is the project's own extension.
        var mapping = RuleMapper.Map("ExactLength", Args("6"));

        Assert.False(mapping.IsSupported);
        Assert.Contains("Length(n)", mapping.UnsupportedReason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Matches", "\"^[A-Z]+$\"")]
    [InlineData("EmailAddress")]
    public void Map_RuleWhoseCompatibilitySemanticsDiffer_IsReported(string rule, params string[] arguments)
    {
        // FluentValidation fails "" for both rules; the compatibility builder skips blank values, and its email
        // pattern rejects addresses FluentValidation accepts (a@b). Migrating either would change verdicts.
        var mapping = RuleMapper.Map(rule, Args(arguments));

        Assert.False(mapping.IsSupported);
        Assert.Null(mapping.TargetMethod);
        Assert.Contains("differs from FluentValidation", mapping.UnsupportedReason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Must", "(x, value) => value > 0")]
    [InlineData("Must", "(x, value, context) => value > 0")]
    [InlineData("When", "(x, context) => x.Age > 0")]
    [InlineData("Unless", "(x, context) => x.Age > 0")]
    public void Map_PredicateLambdaWithMoreThanOneParameter_IsReported(string rule, string lambda)
    {
        // One argument, but the compatibility builder only takes one-parameter delegates.
        var mapping = RuleMapper.Map(rule, Args(lambda));

        Assert.False(mapping.IsSupported);
        Assert.Contains(rule, mapping.UnsupportedReason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\"{PropertyName} is required\"")]
    [InlineData("Messages.Required")]
    [InlineData("$\"at most {Max} characters\"")]
    public void Map_WithMessageThatMayHoldPlaceholders_IsReported(string message)
    {
        // FluentValidation expands {PropertyName} and friends; the compatibility builder prints the text as is.
        var mapping = RuleMapper.Map("WithMessage", Args(message));

        Assert.False(mapping.IsSupported);
        Assert.Contains("placeholders", mapping.UnsupportedReason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Null")]
    [InlineData("Empty")]
    [InlineData("WithName")]
    [InlineData("Cascade")]
    [InlineData("ScalePrecision")]
    [InlineData("PrecisionScale")]
    [InlineData("MustAsync")]
    [InlineData("SetValidator")]
    [InlineData("ChildRules")]
    [InlineData("DependentRules")]
    [InlineData("Custom")]
    [InlineData("SomeCustomExtensionRule")]
    public void Map_UnsupportedRule_IsReportedWithReason(string rule)
    {
        var mapping = RuleMapper.Map(rule, Args());

        Assert.False(mapping.IsSupported);
        Assert.Null(mapping.TargetMethod);
        Assert.False(string.IsNullOrWhiteSpace(mapping.UnsupportedReason));
    }

    [Fact]
    public void Map_MustWithUnsupportedArity_IsReported()
    {
        // Two-argument Must (value, context) has no compatibility equivalent.
        var mapping = RuleMapper.Map("Must", Args("x", "ctx"));

        Assert.False(mapping.IsSupported);
        Assert.Contains("Must", mapping.UnsupportedReason, StringComparison.Ordinal);
    }

    [Fact]
    public void Map_EmailAddressWithMode_IsReported()
    {
        var mapping = RuleMapper.Map("EmailAddress", Args("EmailValidationMode.Net4xRegex"));

        Assert.False(mapping.IsSupported);
    }

    [Theory]
    [InlineData("GreaterThan")]
    [InlineData("GreaterThanOrEqualTo")]
    [InlineData("LessThan")]
    [InlineData("LessThanOrEqualTo")]
    [InlineData("Equal")]
    [InlineData("NotEqual")]
    public void Map_ComparisonRuleWithMemberLambdaOverload_IsReportedNotMistranslated(string rule)
    {
        // Same arity as the supported constant-value overload, but the single argument is a lambda
        // (member comparison). The compatibility builder has no equivalent, so it must be reported.
        var mapping = RuleMapper.Map(rule, Args("x => x.Other"));

        Assert.False(mapping.IsSupported);
        Assert.Null(mapping.TargetMethod);
        Assert.False(string.IsNullOrWhiteSpace(mapping.UnsupportedReason));
    }

    [Theory]
    [InlineData("GreaterThan", "10")]
    [InlineData("LessThan", "100")]
    [InlineData("Equal", "\"TR\"")]
    [InlineData("NotEqual", "\"admin\"")]
    public void Map_ComparisonRuleWithConstantValue_IsSupported(string rule, string value)
    {
        var mapping = RuleMapper.Map(rule, Args(value));

        Assert.True(mapping.IsSupported);
        Assert.Equal(rule, mapping.TargetMethod);
    }

    [Fact]
    public void Map_WithMessageFactoryLambdaOverload_IsReported()
    {
        // WithMessage(x => $"...") is the message-factory overload; only the constant-string form
        // maps onto the compatibility builder.
        var mapping = RuleMapper.Map("WithMessage", Args("x => \"bad\""));

        Assert.False(mapping.IsSupported);
        Assert.Contains("WithMessage", mapping.UnsupportedReason, StringComparison.Ordinal);
    }

    [Fact]
    public void Map_WithMessageConstantString_IsSupported()
    {
        var mapping = RuleMapper.Map("WithMessage", Args("\"required\""));

        Assert.True(mapping.IsSupported);
        Assert.Equal("WithMessage", mapping.TargetMethod);
    }

    [Theory]
    [InlineData("When", "x => x.Age > 0")]
    [InlineData("Unless", "x => x.Age > 0")]
    [InlineData("When", "(x) => x.Age > 0")]
    public void Map_ConditionWithPredicateLambda_IsSupported(string rule, string lambda)
    {
        var mapping = RuleMapper.Map(rule, Args(lambda));

        Assert.True(mapping.IsSupported);
        Assert.Equal(rule, mapping.TargetMethod);
    }

    [Fact]
    public void Map_MustWithPredicateLambda_IsSupported()
    {
        var mapping = RuleMapper.Map("Must", Args("x => x > 0"));

        Assert.True(mapping.IsSupported);
        Assert.Equal("Must", mapping.TargetMethod);
    }

    [Fact]
    public void Map_MustWithNonLambdaSingleArgument_IsReported()
    {
        // A method-group Must(SomePredicate) is single-argument but not a lambda; the builder takes
        // a Func predicate expressed as a lambda, so a non-lambda single argument is reported.
        var mapping = RuleMapper.Map("Must", Args("ExistingPredicate"));

        Assert.False(mapping.IsSupported);
    }
}
