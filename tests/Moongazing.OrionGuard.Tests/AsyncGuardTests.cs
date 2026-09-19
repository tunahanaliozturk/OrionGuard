using Moongazing.OrionGuard.Core;

namespace Moongazing.OrionGuard.Tests;

public class AsyncGuardTests
{
    [Fact]
    public async Task ValidateAsync_ShouldNotDuplicateErrors_WhenCalledTwice()
    {
        var guard = EnsureAsync.That(1, "x").MustAsync(_ => Task.FromResult(false), "bad");

        await guard.ValidateAsync();
        var result = await guard.ValidateAsync();

        Assert.Single(result.Errors);
    }

    [Fact]
    public async Task ValidateAsync_ShouldRunEachRuleOnce_WhenCalledTwice()
    {
        var calls = 0;
        var guard = EnsureAsync.That(1, "x").MustAsync(_ =>
        {
            calls++;
            return Task.FromResult(true);
        }, "bad");

        await guard.ValidateAsync();
        await guard.ValidateAsync();

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ValidateAsync_ShouldRunRuleAddedAfterFirstCall_WhenCalledAgain()
    {
        var guard = EnsureAsync.That(1, "x").MustAsync(_ => Task.FromResult(false), "first");
        await guard.ValidateAsync();

        guard.MustAsync(_ => Task.FromResult(false), "second");
        var result = await guard.ValidateAsync();

        Assert.Equal(new[] { "first", "second" }, result.Errors.Select(e => e.Message));
    }

    [Fact]
    public async Task ValidateAsync_ShouldRetryRule_WhenItThrewOnPreviousCall()
    {
        var attempts = 0;
        var guard = EnsureAsync.That(1, "x").MustAsync(_ =>
        {
            attempts++;
            return attempts == 1
                ? Task.FromException<bool>(new TimeoutException())
                : Task.FromResult(false);
        }, "bad");

        await Assert.ThrowsAsync<TimeoutException>(() => guard.ValidateAsync());
        var result = await guard.ValidateAsync();

        Assert.Equal(2, attempts);
        Assert.Single(result.Errors);
    }
}
