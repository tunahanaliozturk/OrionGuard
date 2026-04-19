using System.Collections.Generic;
using Moongazing.OrionGuard.Domain.Primitives;

namespace Moongazing.OrionGuard.Tests;

public class ValueObjectTests
{
    private sealed record RecordAddress(string Street, string City) : IValueObject;

    [Fact]
    public void IValueObject_ShouldBeImplementableByRecord_WhenMarkerAppliedToRecord()
    {
        IValueObject vo = new RecordAddress("Main St", "Ankara");

        Assert.NotNull(vo);
        Assert.IsAssignableFrom<IValueObject>(vo);
    }

    private sealed class Money : ValueObject
    {
        public decimal Amount { get; }
        public string Currency { get; }

        public Money(decimal amount, string currency)
        {
            Amount = amount;
            Currency = currency;
        }

        protected override IEnumerable<object?> GetEqualityComponents()
        {
            yield return Amount;
            yield return Currency;
        }
    }

    [Fact]
    public void Equals_ShouldReturnTrue_WhenComponentsMatch()
    {
        var a = new Money(100m, "TRY");
        var b = new Money(100m, "TRY");

        Assert.True(a.Equals(b));
        Assert.True(a == b);
        Assert.False(a != b);
    }

    [Fact]
    public void Equals_ShouldReturnFalse_WhenAnyComponentDiffers()
    {
        var a = new Money(100m, "TRY");
        var b = new Money(100m, "USD");

        Assert.False(a.Equals(b));
        Assert.True(a != b);
    }

    [Fact]
    public void GetHashCode_ShouldBeEqual_WhenComponentsMatch()
    {
        var a = new Money(42m, "EUR");
        var b = new Money(42m, "EUR");

        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Equals_ShouldReturnFalse_WhenOtherIsNull()
    {
        var a = new Money(1m, "TRY");
        Money? b = null;

        Assert.False(a.Equals(b));
        Assert.False(a == b);
        Assert.False(b == a);
    }

    [Fact]
    public void Equals_ShouldReturnFalse_WhenOtherIsDifferentType()
    {
        var money = new Money(1m, "TRY");
        object other = "not a value object";

        Assert.False(money.Equals(other));
    }
}
