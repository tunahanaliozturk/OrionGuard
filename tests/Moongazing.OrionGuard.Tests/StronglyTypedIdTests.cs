using Moongazing.OrionGuard.Domain.Primitives;

namespace Moongazing.OrionGuard.Tests;

public class StronglyTypedIdTests
{
    private sealed record OrderId(Guid Value) : StronglyTypedId<Guid>(Value);
    private sealed record CustomerId(Guid Value) : StronglyTypedId<Guid>(Value);
    private sealed record IntegerId(int Value) : StronglyTypedId<int>(Value);

    [Fact]
    public void Equals_ShouldReturnTrue_WhenValuesAndTypesMatch()
    {
        var g = Guid.NewGuid();
        var a = new OrderId(g);
        var b = new OrderId(g);

        Assert.Equal(a, b);
        Assert.True(a == b);
    }

    [Fact]
    public void Equals_ShouldReturnFalse_WhenTypesDiffer()
    {
        var g = Guid.NewGuid();
        OrderId a = new(g);
        CustomerId b = new(g);

        Assert.NotEqual<object>(a, b);
    }

    [Fact]
    public void Equals_ShouldReturnFalse_WhenValuesDiffer()
    {
        var a = new IntegerId(1);
        var b = new IntegerId(2);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void ToString_ShouldIncludeValue_WhenCalled()
    {
        var id = new IntegerId(42);

        Assert.Contains("42", id.ToString());
    }
}
