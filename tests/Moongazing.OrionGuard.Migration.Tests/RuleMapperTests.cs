using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Moongazing.OrionGuard.Migration;

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
    [InlineData("Matches", 1, "Matches")]
    [InlineData("EmailAddress", 0, "EmailAddress")]
    [InlineData("GreaterThan", 1, "GreaterThan")]
    [InlineData("GreaterThanOrEqualTo", 1, "GreaterThanOrEqualTo")]
    [InlineData("LessThan", 1, "LessThan")]
    [InlineData("LessThanOrEqualTo", 1, "LessThanOrEqualTo")]
    [InlineData("InclusiveBetween", 2, "InclusiveBetween")]
    [InlineData("ExclusiveBetween", 2, "ExclusiveBetween")]
    [InlineData("Must", 1, "Must")]
    [InlineData("WithMessage", 1, "WithMessage")]
    [InlineData("WithErrorCode", 1, "WithErrorCode")]
    [InlineData("When", 1, "When")]
    [InlineData("Unless", 1, "Unless")]
    public void Map_SupportedRule_ReturnsTargetMethod(string rule, int argCount, string expectedTarget)
    {
        var args = Args(Enumerable.Repeat("0", argCount).ToArray());

        var mapping = RuleMapper.Map(rule, args);

        Assert.True(mapping.IsSupported);
        Assert.Equal(expectedTarget, mapping.TargetMethod);
        Assert.Equal(ArgumentTransform.Verbatim, mapping.ArgumentTransform);
    }

    [Fact]
    public void Map_ExactLength_MapsToLengthWithArgumentDuplication()
    {
        var mapping = RuleMapper.Map("ExactLength", Args("6"));

        Assert.True(mapping.IsSupported);
        Assert.Equal("Length", mapping.TargetMethod);
        Assert.Equal(ArgumentTransform.DuplicateSingleArgument, mapping.ArgumentTransform);
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
}
