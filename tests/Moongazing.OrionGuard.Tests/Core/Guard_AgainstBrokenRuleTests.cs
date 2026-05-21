using Moongazing.OrionGuard.Core;
using Moongazing.OrionGuard.Domain.Exceptions;
using Moongazing.OrionGuard.Domain.Rules;

namespace Moongazing.OrionGuard.Tests.Core;

public class Guard_AgainstBrokenRuleTests
{
    private sealed class StubRule(bool isBroken) : BusinessRule
    {
        public override bool IsBroken() => isBroken;
        public override string DefaultMessage => "stub broken";
    }

    private sealed class StubAsyncRule(bool isBroken) : AsyncBusinessRule
    {
        public override Task<bool> IsBrokenAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(isBroken);
        public override string DefaultMessage => "stub broken async";
    }

    [Fact]
    public void AgainstBrokenRule_ShouldNotThrow_WhenRuleIsNotBroken()
    {
        Guard.AgainstBrokenRule(new StubRule(false));
    }

    [Fact]
    public void AgainstBrokenRule_ShouldThrowBusinessRuleValidationException_WhenRuleIsBroken()
    {
        var ex = Assert.Throws<BusinessRuleValidationException>(
            () => Guard.AgainstBrokenRule(new StubRule(true)));
        Assert.Equal(nameof(StubRule), ex.RuleName);
        Assert.Equal(nameof(StubRule), ex.MessageKey);
        Assert.Equal("stub broken", ex.Message);
    }

    [Fact]
    public void AgainstBrokenRule_ShouldThrow_WhenRuleIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => Guard.AgainstBrokenRule(null!));
    }

    [Fact]
    public async Task AgainstBrokenRuleAsync_ShouldNotThrow_WhenRuleIsNotBroken()
    {
        await Guard.AgainstBrokenRuleAsync(new StubAsyncRule(false));
    }

    [Fact]
    public async Task AgainstBrokenRuleAsync_ShouldThrowBusinessRuleValidationException_WhenRuleIsBroken()
    {
        var ex = await Assert.ThrowsAsync<BusinessRuleValidationException>(
            () => Guard.AgainstBrokenRuleAsync(new StubAsyncRule(true)));
        Assert.Equal(nameof(StubAsyncRule), ex.RuleName);
        Assert.Equal("stub broken async", ex.Message);
    }

    [Fact]
    public async Task AgainstBrokenRuleAsync_ShouldThrow_WhenRuleIsNull()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => Guard.AgainstBrokenRuleAsync(null!));
    }

    [Fact]
    public async Task AgainstBrokenRuleAsync_ShouldRespectCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => Guard.AgainstBrokenRuleAsync(new CancelObservingRule(), cts.Token));
    }

    private sealed class CancelObservingRule : AsyncBusinessRule
    {
        public override Task<bool> IsBrokenAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(false);
        }
        public override string DefaultMessage => "x";
    }
}
