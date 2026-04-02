using Moongazing.OrionGuard.Core;
using Moongazing.OrionGuard.Exceptions;

namespace Moongazing.OrionGuard.Tests;

public class FluentGuardTests
{
    #region Core Validations

    [Fact]
    public void NotNull_ShouldThrow_WhenValueIsNull()
    {
        Assert.Throws<GuardException>(() =>
            Ensure.That<string?>(null).NotNull().Build());
    }

    [Fact]
    public void NotNull_ShouldNotThrow_WhenValueIsNotNull()
    {
        var result = Ensure.That("hello").NotNull().Build();
        Assert.Equal("hello", result);
    }

    [Fact]
    public void NotDefault_ShouldThrow_WhenValueIsDefault()
    {
        Assert.Throws<GuardException>(() =>
            Ensure.That(0).NotDefault().Build());
    }

    [Fact]
    public void Must_ShouldThrow_WhenPredicateFails()
    {
        Assert.Throws<GuardException>(() =>
            Ensure.That(5).Must(v => v > 10, "Must be greater than 10").Build());
    }

    [Fact]
    public void Must_ShouldNotThrow_WhenPredicateSucceeds()
    {
        var result = Ensure.That(15).Must(v => v > 10, "Must be greater than 10").Build();
        Assert.Equal(15, result);
    }

    #endregion

    #region String Validations

    [Fact]
    public void NotEmpty_ShouldThrow_WhenStringIsWhitespace()
    {
        Assert.Throws<GuardException>(() =>
            Ensure.That("   ").NotEmpty().Build());
    }

    [Fact]
    public void Length_ShouldThrow_WhenStringIsTooShort()
    {
        Assert.Throws<GuardException>(() =>
            Ensure.That("hi").Length(5, 10).Build());
    }

    [Fact]
    public void Length_ShouldThrow_WhenStringIsTooLong()
    {
        Assert.Throws<GuardException>(() =>
            Ensure.That("this is a very long string").Length(1, 5).Build());
    }

    [Fact]
    public void MinLength_ShouldThrow_WhenBelowMinimum()
    {
        Assert.Throws<GuardException>(() =>
            Ensure.That("ab").MinLength(5).Build());
    }

    [Fact]
    public void MaxLength_ShouldThrow_WhenAboveMaximum()
    {
        Assert.Throws<GuardException>(() =>
            Ensure.That("abcdef").MaxLength(3).Build());
    }

    [Fact]
    public void Email_ShouldThrow_WhenInvalidEmail()
    {
        Assert.Throws<GuardException>(() =>
            Ensure.That("not-an-email").Email().Build());
    }

    [Fact]
    public void Email_ShouldNotThrow_WhenValidEmail()
    {
        var result = Ensure.That("test@example.com").Email().Build();
        Assert.Equal("test@example.com", result);
    }

    [Fact]
    public void StartsWith_ShouldThrow_WhenDoesNotStartWith()
    {
        Assert.Throws<GuardException>(() =>
            Ensure.That("hello").StartsWith("world").Build());
    }

    [Fact]
    public void EndsWith_ShouldThrow_WhenDoesNotEndWith()
    {
        Assert.Throws<GuardException>(() =>
            Ensure.That("hello").EndsWith("world").Build());
    }

    [Fact]
    public void Contains_ShouldThrow_WhenDoesNotContain()
    {
        Assert.Throws<GuardException>(() =>
            Ensure.That("hello").Contains("xyz").Build());
    }

    [Fact]
    public void Matches_ShouldThrow_WhenPatternDoesNotMatch()
    {
        Assert.Throws<GuardException>(() =>
            Ensure.That("abc").Matches(@"^\d+$").Build());
    }

    #endregion

    #region Numeric Validations

    [Fact]
    public void GreaterThan_ShouldThrow_WhenNotGreater()
    {
        Assert.Throws<GuardException>(() =>
            Ensure.That(5).GreaterThan(10).Build());
    }

    [Fact]
    public void LessThan_ShouldThrow_WhenNotLess()
    {
        Assert.Throws<GuardException>(() =>
            Ensure.That(15).LessThan(10).Build());
    }

    [Fact]
    public void InRange_ShouldThrow_WhenOutOfRange()
    {
        Assert.Throws<GuardException>(() =>
            Ensure.That(25).InRange(1, 10).Build());
    }

    [Fact]
    public void Positive_ShouldThrow_WhenNegative()
    {
        Assert.Throws<GuardException>(() =>
            Ensure.That(-5).Positive().Build());
    }

    [Fact]
    public void NotNegative_ShouldThrow_WhenNegative()
    {
        Assert.Throws<GuardException>(() =>
            Ensure.That(-1).NotNegative().Build());
    }

    [Fact]
    public void NotZero_ShouldThrow_WhenZero()
    {
        Assert.Throws<GuardException>(() =>
            Ensure.That(0).NotZero().Build());
    }

    #endregion

    #region Conditional Validation

    [Fact]
    public void When_ShouldSkipValidation_WhenConditionIsFalse()
    {
        // Should NOT throw despite the value being negative
        var result = Ensure.That(-5).When(false).Positive().Build();
        Assert.Equal(-5, result);
    }

    [Fact]
    public void When_ShouldApplyValidation_WhenConditionIsTrue()
    {
        Assert.Throws<GuardException>(() =>
            Ensure.That(-5).When(true).Positive().Build());
    }

    [Fact]
    public void Unless_ShouldSkipValidation_WhenConditionIsTrue()
    {
        var result = Ensure.That(-5).Unless(true).Positive().Build();
        Assert.Equal(-5, result);
    }

    [Fact]
    public void Always_ShouldReenableValidation()
    {
        Assert.Throws<GuardException>(() =>
            Ensure.That(-5).When(false).Always().Positive().Build());
    }

    [Fact]
    public void When_WithPredicate_ShouldEvaluateValue()
    {
        // When value > 0, validate it must be > 10 — value is 5, so condition is true, validation fires
        Assert.Throws<GuardException>(() =>
            Ensure.That(5).When(v => v > 0).GreaterThan(10).Build());
    }

    #endregion

    #region Transform / Default

    [Fact]
    public void Transform_ShouldModifyValue()
    {
        var result = Ensure.That("  HELLO  ")
            .Transform(v => v.Trim())
            .Transform(v => v.ToLowerInvariant())
            .Build();
        Assert.Equal("hello", result);
    }

    [Fact]
    public void Default_ShouldReplaceNull_WithDefaultValue()
    {
        var result = Ensure.That<string?>(null)
            .Default("fallback")
            .Build();
        Assert.Equal("fallback", result);
    }

    [Fact]
    public void Default_ShouldKeepOriginalValue_WhenNotNull()
    {
        var result = Ensure.That<string?>("original")
            .Default("fallback")
            .Build();
        Assert.Equal("original", result);
    }

    #endregion

    #region Result Pattern / Error Accumulation

    [Fact]
    public void Accumulate_ShouldCollectAllErrors()
    {
        var result = Ensure.Accumulate("")
            .NotEmpty()
            .MinLength(5)
            .ToResult();

        Assert.True(result.IsInvalid);
        Assert.Equal(2, result.Errors.Count);
    }

    [Fact]
    public void TryValidate_ShouldReturnFalse_WhenInvalid()
    {
        var guard = Ensure.Accumulate("").NotEmpty();
        var isValid = guard.TryValidate(out var value, out var errors);

        Assert.False(isValid);
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void TryValidate_ShouldReturnTrue_WhenValid()
    {
        var guard = Ensure.Accumulate("hello").NotEmpty();
        var isValid = guard.TryValidate(out var value, out var errors);

        Assert.True(isValid);
        Assert.Empty(errors);
        Assert.Equal("hello", value);
    }

    [Fact]
    public void ImplicitConversion_ShouldReturnValue()
    {
        string result = Ensure.That("hello").NotEmpty();
        Assert.Equal("hello", result);
    }

    #endregion

    #region Collection Validations

    [Fact]
    public void Count_ShouldThrow_WhenCountDoesNotMatch()
    {
        Assert.Throws<GuardException>(() =>
            Ensure.That(new List<int> { 1, 2, 3 }).Count(5).Build());
    }

    [Fact]
    public void MinCount_ShouldThrow_WhenBelowMinimum()
    {
        Assert.Throws<GuardException>(() =>
            Ensure.That(new List<int> { 1 }).MinCount(3).Build());
    }

    [Fact]
    public void MaxCount_ShouldThrow_WhenAboveMaximum()
    {
        Assert.Throws<GuardException>(() =>
            Ensure.That(new List<int> { 1, 2, 3, 4, 5 }).MaxCount(2).Build());
    }

    [Fact]
    public void NoNullItems_ShouldThrow_WhenContainsNull()
    {
        Assert.Throws<GuardException>(() =>
            Ensure.That(new List<string?> { "a", null, "b" }).NoNullItems().Build());
    }

    #endregion

    #region Chaining

    [Fact]
    public void MethodChaining_ShouldWorkEndToEnd()
    {
        var email = "test@example.com";
        var result = Ensure.That(email)
            .NotNull()
            .NotEmpty()
            .MinLength(5)
            .MaxLength(100)
            .Email()
            .Build();

        Assert.Equal(email, result);
    }

    #endregion
}
