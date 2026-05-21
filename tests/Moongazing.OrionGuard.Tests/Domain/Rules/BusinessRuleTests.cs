using Moongazing.OrionGuard.Domain.Rules;

namespace Moongazing.OrionGuard.Tests.Domain.Rules;

public class BusinessRuleTests
{
    private sealed class OrderMustHaveItems(bool isBroken) : BusinessRule
    {
        public override bool IsBroken() => isBroken;
        public override string DefaultMessage => "An order must have at least one item.";
    }

    private sealed class CustomKeyRule : BusinessRule
    {
        public override bool IsBroken() => true;
        public override string DefaultMessage => "x";
        public override string MessageKey => "custom_key";
    }

    private sealed class WithArgsRule(int min) : BusinessRule
    {
        public override bool IsBroken() => true;
        public override string DefaultMessage => "must be >= {0}";
        public override object[] MessageArgs => new object[] { min };
    }

    [Fact]
    public void IsBroken_ShouldReturnSubclassImplementation()
    {
        Assert.True(new OrderMustHaveItems(true).IsBroken());
        Assert.False(new OrderMustHaveItems(false).IsBroken());
    }

    [Fact]
    public void MessageKey_ShouldDefaultToTypeName()
    {
        var rule = new OrderMustHaveItems(true);
        Assert.Equal(nameof(OrderMustHaveItems), rule.MessageKey);
    }

    [Fact]
    public void MessageKey_ShouldRespectOverride()
    {
        Assert.Equal("custom_key", new CustomKeyRule().MessageKey);
    }

    [Fact]
    public void MessageArgs_ShouldDefaultToNull()
    {
        Assert.Null(new OrderMustHaveItems(true).MessageArgs);
    }

    [Fact]
    public void MessageArgs_ShouldRespectOverride()
    {
        Assert.Equal(new object[] { 5 }, new WithArgsRule(5).MessageArgs);
    }
}
