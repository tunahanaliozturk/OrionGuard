using Moongazing.OrionGuard.Extensions;

namespace Moongazing.OrionGuard.Tests;

public class BusinessGuardsTests
{
    #region AgainstInvalidMonetaryAmount

    [Theory]
    [InlineData("10.5000")]
    [InlineData("10.50")]
    [InlineData("10")]
    [InlineData("0.00")]
    [InlineData("1.2300000000")]
    public void AgainstInvalidMonetaryAmount_ShouldNotThrow_WhenTrailingZerosExceedScale(string amount)
    {
        var value = decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Null(Record.Exception(() => value.AgainstInvalidMonetaryAmount("amount")));
    }

    [Theory]
    [InlineData("10.505")]
    [InlineData("0.001")]
    [InlineData("-1")]
    public void AgainstInvalidMonetaryAmount_ShouldStillThrow_WhenAmountHasMorePlacesOrIsNegative(string amount)
    {
        var value = decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Throws<ArgumentException>(() => value.AgainstInvalidMonetaryAmount("amount"));
    }

    [Fact]
    public void AgainstInvalidMonetaryAmount_ShouldHonorCustomPlaces_WhenScaleIsZero()
    {
        Assert.Null(Record.Exception(() => 42.000m.AgainstInvalidMonetaryAmount("amount", maxDecimalPlaces: 0)));
        Assert.Throws<ArgumentException>(() => 42.5m.AgainstInvalidMonetaryAmount("amount", maxDecimalPlaces: 0));
    }

    #endregion
}
