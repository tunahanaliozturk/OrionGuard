using Moongazing.OrionGuard.Core;
using Moongazing.OrionGuard.DependencyInjection;

namespace Moongazing.OrionGuard.Tests;

/// <summary>
/// BUG-C1: comparison guards infer the threshold type from the literal (GreaterThan(0) is
/// GreaterThan&lt;int&gt;), and used to skip the rule whenever the value was of another numeric type.
/// </summary>
public class NumericComparisonTests
{
    private sealed class Order
    {
        public decimal Price { get; set; }
    }

    private sealed class PriceValidator : AbstractValidator<Order>
    {
        public PriceValidator()
        {
            RuleFor(o => o.Price, "Price", p => p.GreaterThan(0));
        }
    }

    /// <summary>A negative value of every built-in signed numeric type, and a zero of every unsigned one.</summary>
    public static TheoryData<object> ValuesNotAboveZero => new()
    {
        (sbyte)-5, (short)-5, -5L, -5.5f, -5.5d, -5.5m,
        (byte)0, (ushort)0, 0u, 0UL,
    };

    /// <summary>A positive value of every built-in numeric type.</summary>
    public static TheoryData<object> ValuesAboveZero => new()
    {
        (sbyte)5, (byte)5, (short)5, (ushort)5, 5, 5u, 5L, 5UL, 5.5f, 5.5d, 5.5m,
    };

    #region FluentGuard

    [Fact]
    public void GreaterThan_ShouldCompareExactly_WhenLongValueIsAboveDoublePrecision()
    {
        // 2^53 + 1 and 2^53 collapse to the same double; the integer side must not be rounded.
        Assert.True(Ensure.Accumulate(9007199254740993L, "id").GreaterThan(9007199254740992d).ToResult().IsValid);
        Assert.True(Ensure.Accumulate(9007199254740992L, "id").GreaterThan(9007199254740992d).ToResult().IsInvalid);
    }

    [Fact]
    public void GreaterThan_ShouldHonourTheFraction_WhenIntValueIsComparedWithDoubleLiteral()
    {
        Assert.True(Ensure.Accumulate(5, "qty").GreaterThan(4.5).ToResult().IsValid);
        Assert.True(Ensure.Accumulate(4, "qty").GreaterThan(4.5).ToResult().IsInvalid);
        Assert.True(Ensure.Accumulate(-5, "qty").LessThan(-4.5).ToResult().IsValid);
    }

    [Fact]
    public void InRange_ShouldTreatDoubleLiteralAsItsDecimalValue_WhenValueIsDecimal()
    {
        // The literal 0.1 is the binary 0.1000000000000000055...; a decimal 0.1m must still be in range.
        Assert.True(Ensure.Accumulate(0.1m, "rate").InRange(0.1, 1.0).ToResult().IsValid);
        Assert.True(Ensure.Accumulate(0.10000000000000001m, "rate").GreaterThan(0.1).ToResult().IsValid);
    }

    [Fact]
    public void GreaterThan_ShouldFail_WhenDecimalValueIsBelowIntLiteral()
    {
        var result = Ensure.Accumulate(-5m, "price").GreaterThan(0).ToResult();

        Assert.True(result.IsInvalid);
        Assert.Equal("GREATER_THAN", Assert.Single(result.Errors).ErrorCode);
    }

    [Fact]
    public void GreaterThan_ShouldFail_WhenDoubleValueIsBelowIntLiteral()
    {
        Assert.True(Ensure.Accumulate(-1.0, "d").GreaterThan(0).ToResult().IsInvalid);
    }

    [Fact]
    public void InRange_ShouldFail_WhenLongValueIsAboveIntRange()
    {
        Assert.True(Ensure.Accumulate(500L, "qty").InRange(1, 10).ToResult().IsInvalid);
    }

    [Fact]
    public void LessThan_ShouldFail_WhenDecimalValueIsAboveIntLiteral()
    {
        Assert.True(Ensure.Accumulate(15.5m, "price").LessThan(10).ToResult().IsInvalid);
    }

    [Theory]
    [MemberData(nameof(ValuesNotAboveZero))]
    public void GreaterThan_ShouldFail_WhenAnyNumericTypeIsNotAboveIntLiteral(object value)
    {
        Assert.True(Ensure.Accumulate(value, "v").GreaterThan(0).ToResult().IsInvalid);
    }

    [Theory]
    [MemberData(nameof(ValuesAboveZero))]
    public void GreaterThan_ShouldPass_WhenAnyNumericTypeIsAboveIntLiteral(object value)
    {
        Assert.True(Ensure.Accumulate(value, "v").GreaterThan(0).ToResult().IsValid);
    }

    [Fact]
    public void GreaterThan_ShouldPass_WhenDecimalValueIsAboveIntLiteral()
    {
        Assert.True(Ensure.Accumulate(0.01m, "price").GreaterThan(0).ToResult().IsValid);
    }

    [Fact]
    public void InRange_ShouldPass_WhenLongValueIsInsideIntRange()
    {
        Assert.True(Ensure.Accumulate(5L, "qty").InRange(1, 10).ToResult().IsValid);
    }

    [Fact]
    public void GreaterThan_ShouldCompareCorrectly_WhenSignedAndUnsignedTypesMix()
    {
        Assert.True(Ensure.Accumulate(ulong.MaxValue, "v").GreaterThan(-1).ToResult().IsValid);
        Assert.True(Ensure.Accumulate(-1, "v").GreaterThan(0UL).ToResult().IsInvalid);
    }

    [Fact]
    public void LessThan_ShouldCompareExactly_WhenDecimalThresholdMeetsIntValue()
    {
        Assert.True(Ensure.Accumulate(10, "v").LessThan(10.000000000000000000001m).ToResult().IsValid);
        Assert.True(Ensure.Accumulate(10, "v").LessThan(10m).ToResult().IsInvalid);
    }

    [Fact]
    public void GreaterThan_ShouldPass_WhenValueIsNull()
    {
        // Null is NotNull()'s concern; a comparison rule does not report it.
        Assert.True(Ensure.Accumulate((decimal?)null, "price").GreaterThan(0).ToResult().IsValid);
    }

    [Fact]
    public void GreaterThan_ShouldFail_WhenValueTypeIsNotComparableWithThreshold()
    {
        Assert.True(Ensure.Accumulate("abc", "name").GreaterThan(0).ToResult().IsInvalid);
    }

    [Fact]
    public void GreaterThan_ShouldStillCompareStrings_WhenValueAndThresholdAreStrings()
    {
        Assert.True(Ensure.Accumulate("b", "name").GreaterThan("a").ToResult().IsValid);
    }

    #endregion

    #region NaN and small integer types

    [Fact]
    public void Positive_ShouldFail_WhenDoubleIsNaN()
    {
        Assert.True(Ensure.Accumulate(double.NaN, "d").Positive().ToResult().IsInvalid);
    }

    [Fact]
    public void NotNegative_ShouldFail_WhenDoubleIsNaN()
    {
        Assert.True(Ensure.Accumulate(double.NaN, "d").NotNegative().ToResult().IsInvalid);
    }

    [Fact]
    public void Positive_ShouldFail_WhenFloatIsNaN()
    {
        Assert.True(Ensure.Accumulate(float.NaN, "f").Positive().ToResult().IsInvalid);
    }

    [Fact]
    public void InRange_ShouldFail_WhenDoubleIsNaN()
    {
        Assert.True(Ensure.Accumulate(double.NaN, "d").InRange(0, 10).ToResult().IsInvalid);
    }

    [Fact]
    public void LessThan_ShouldFail_WhenDoubleIsNaN()
    {
        Assert.True(Ensure.Accumulate(double.NaN, "d").LessThan(10.0).ToResult().IsInvalid);
    }

    [Fact]
    public void Positive_ShouldFail_WhenShortIsNegative()
    {
        Assert.True(Ensure.Accumulate((short)-5, "s").Positive().ToResult().IsInvalid);
    }

    [Fact]
    public void NotNegative_ShouldFail_WhenSbyteIsNegative()
    {
        Assert.True(Ensure.Accumulate((sbyte)-5, "s").NotNegative().ToResult().IsInvalid);
    }

    [Fact]
    public void Positive_ShouldFail_WhenByteIsZero()
    {
        Assert.True(Ensure.Accumulate((byte)0, "b").Positive().ToResult().IsInvalid);
    }

    [Fact]
    public void NotZero_ShouldFail_WhenUlongIsZero()
    {
        Assert.True(Ensure.Accumulate(0UL, "u").NotZero().ToResult().IsInvalid);
    }

    [Fact]
    public void Positive_ShouldPass_WhenSmallIntegerTypesArePositive()
    {
        Assert.True(Ensure.Accumulate((short)5, "s").Positive().ToResult().IsValid);
        Assert.True(Ensure.Accumulate((byte)5, "b").Positive().ToResult().IsValid);
        Assert.True(Ensure.Accumulate((sbyte)5, "s").NotNegative().ToResult().IsValid);
    }

    [Fact]
    public void Positive_ShouldPass_WhenDoubleIsPositiveInfinity()
    {
        Assert.True(Ensure.Accumulate(double.PositiveInfinity, "d").Positive().ToResult().IsValid);
    }

    #endregion

    #region PropertyValidator (AbstractValidator.RuleFor)

    [Fact]
    public void RuleForGreaterThan_ShouldFail_WhenDecimalPropertyIsBelowIntLiteral()
    {
        Assert.True(new PriceValidator().Validate(new Order { Price = -10m }).IsInvalid);
    }

    [Fact]
    public void RuleForGreaterThan_ShouldPass_WhenDecimalPropertyIsAboveIntLiteral()
    {
        Assert.True(new PriceValidator().Validate(new Order { Price = 10m }).IsValid);
    }

    #endregion

    #region NestedValidator

    [Fact]
    public void NestedGreaterThan_ShouldFail_WhenDecimalPropertyIsBelowIntLiteral()
    {
        var result = Validate.Nested(new Order { Price = -10m })
            .Property(o => o.Price, p => p.GreaterThan(0))
            .ToResult();

        Assert.True(result.IsInvalid);
    }

    [Fact]
    public void NestedLessThan_ShouldFail_WhenDecimalPropertyIsAboveIntLiteral()
    {
        var result = Validate.Nested(new Order { Price = 10m })
            .Property(o => o.Price, p => p.LessThan(5))
            .ToResult();

        Assert.True(result.IsInvalid);
    }

    [Fact]
    public void NestedInRange_ShouldFail_WhenDecimalPropertyIsOutsideIntRange()
    {
        var result = Validate.Nested(new Order { Price = 500m })
            .Property(o => o.Price, p => p.InRange(1, 10))
            .ToResult();

        Assert.True(result.IsInvalid);
    }

    [Fact]
    public void NestedInRange_ShouldPass_WhenDecimalPropertyIsInsideIntRange()
    {
        var result = Validate.Nested(new Order { Price = 5.5m })
            .Property(o => o.Price, p => p.InRange(1, 10))
            .ToResult();

        Assert.True(result.IsValid);
    }

    [Fact]
    public void NestedGreaterThanIComparable_ShouldFailInsteadOfThrowing_WhenTypesDiffer()
    {
        // The IComparable overload used to throw ArgumentException ("Object must be of type Decimal").
        var result = Validate.Nested(new Order { Price = -10m })
            .Property(o => o.Price, p => p.GreaterThan((IComparable)0))
            .ToResult();

        Assert.True(result.IsInvalid);
    }

    #endregion
}
