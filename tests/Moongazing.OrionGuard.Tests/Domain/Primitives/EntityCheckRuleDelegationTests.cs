using Moongazing.OrionGuard.Domain.Exceptions;
using Moongazing.OrionGuard.Domain.Primitives;
using Moongazing.OrionGuard.Domain.Rules;

namespace Moongazing.OrionGuard.Tests.Domain.Primitives;

public class EntityCheckRuleDelegationTests
{
    private sealed class TestEntity : Entity<int>
    {
        public TestEntity(int id) : base(id) { }

        public void EnforceSync(IBusinessRule rule) => CheckRule(rule);
        public Task EnforceAsync(IAsyncBusinessRule rule, CancellationToken ct = default)
            => CheckRuleAsync(rule, ct);
    }

    private sealed class StubRule(bool isBroken) : BusinessRule
    {
        public override bool IsBroken() => isBroken;
        public override string DefaultMessage => "broken";
    }

    private sealed class StubAsyncRule(bool isBroken) : AsyncBusinessRule
    {
        public override Task<bool> IsBrokenAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(isBroken);
        public override string DefaultMessage => "broken async";
    }

    [Fact]
    public void CheckRule_ShouldNotThrow_WhenRuleIsNotBroken()
    {
        new TestEntity(1).EnforceSync(new StubRule(false));
    }

    [Fact]
    public void CheckRule_ShouldThrowBusinessRuleValidationException_WhenRuleIsBroken()
    {
        var ex = Assert.Throws<BusinessRuleValidationException>(
            () => new TestEntity(1).EnforceSync(new StubRule(true)));
        Assert.Equal(nameof(StubRule), ex.RuleName);
        Assert.Equal("broken", ex.Message);
    }

    [Fact]
    public void CheckRule_ShouldThrow_WhenRuleIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new TestEntity(1).EnforceSync(null!));
    }

    [Fact]
    public async Task CheckRuleAsync_ShouldThrowBusinessRuleValidationException_WhenRuleIsBroken()
    {
        var ex = await Assert.ThrowsAsync<BusinessRuleValidationException>(
            () => new TestEntity(1).EnforceAsync(new StubAsyncRule(true)));
        Assert.Equal(nameof(StubAsyncRule), ex.RuleName);
    }

    [Fact]
    public async Task CheckRuleAsync_ShouldThrow_WhenRuleIsNull()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => new TestEntity(1).EnforceAsync(null!));
    }
}
