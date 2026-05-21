using Moongazing.OrionGuard.Domain.Rules;

namespace Moongazing.OrionGuard.Tests.Domain.Rules;

public class AsyncBusinessRuleTests
{
    private sealed class UniqueEmailRule(bool isBroken) : AsyncBusinessRule
    {
        public override Task<bool> IsBrokenAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(isBroken);
        public override string DefaultMessage => "Email must be unique.";
    }

    [Fact]
    public async Task IsBrokenAsync_ShouldReturnSubclassImplementation()
    {
        Assert.True(await new UniqueEmailRule(true).IsBrokenAsync());
        Assert.False(await new UniqueEmailRule(false).IsBrokenAsync());
    }

    [Fact]
    public void MessageKey_ShouldDefaultToTypeName()
    {
        var rule = new UniqueEmailRule(true);
        Assert.Equal(nameof(UniqueEmailRule), rule.MessageKey);
    }

    [Fact]
    public async Task IsBrokenAsync_ShouldRespectCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var rule = new ThrowingRule();
        await Assert.ThrowsAsync<OperationCanceledException>(() => rule.IsBrokenAsync(cts.Token));
    }

    private sealed class ThrowingRule : AsyncBusinessRule
    {
        public override Task<bool> IsBrokenAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(false);
        }
        public override string DefaultMessage => "x";
    }
}
