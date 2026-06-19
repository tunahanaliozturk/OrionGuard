using Moongazing.OrionGuard.Core;

namespace Moongazing.OrionGuard.Tests;

/// <summary>
/// Tests for the asynchronous validation pipeline added to <see cref="ObjectValidator{T}"/>:
/// async rule pass/fail, cancellation, mixed sync+async aggregation, and short-circuit parity
/// with the synchronous strict path.
/// </summary>
public class ObjectValidatorAsyncTests
{
    private sealed class User
    {
        public string? Email { get; set; }
        public string? Username { get; set; }
        public int Age { get; set; }
    }

    private static Task<bool> Async(bool value) => Task.FromResult(value);

    #region Async Rule Pass / Fail

    [Fact]
    public async Task MustAsync_ShouldReturnValid_WhenAsyncRulePasses()
    {
        var user = new User { Email = "free@example.com" };

        var result = await Validate.For(user)
            .MustAsync(u => u.Email, (email, _) => Async(true), "Email already in use", "EMAIL_TAKEN")
            .ToResultAsync();

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task MustAsync_ShouldFail_WhenAsyncRuleFails()
    {
        var user = new User { Email = "taken@example.com" };

        var result = await Validate.For(user)
            .MustAsync(u => u.Email, (email, _) => Async(false), "Email already in use", "EMAIL_TAKEN")
            .ToResultAsync();

        Assert.True(result.IsInvalid);
        var error = Assert.Single(result.Errors);
        Assert.Equal("Email", error.ParameterName);
        Assert.Equal("Email already in use", error.Message);
        Assert.Equal("EMAIL_TAKEN", error.ErrorCode);
    }

    [Fact]
    public async Task MustAsync_ShouldDefaultErrorCode_WhenNoneSupplied()
    {
        var user = new User { Email = "x@example.com" };

        var result = await Validate.For(user)
            .MustAsync(u => u.Email, (email, _) => Async(false), "bad")
            .ToResultAsync();

        var error = Assert.Single(result.Errors);
        Assert.Equal("ASYNC_PREDICATE", error.ErrorCode);
    }

    [Fact]
    public async Task MustAsync_InstanceLevel_ShouldFail_WhenPredicateFails()
    {
        var user = new User { Email = "a@b.com", Username = "a" };

        var result = await Validate.For(user)
            .MustAsync(
                (u, _) => Async(false),
                "Email and username combination is taken",
                "COMBO",
                "COMBO_TAKEN")
            .ToResultAsync();

        Assert.True(result.IsInvalid);
        var error = Assert.Single(result.Errors);
        Assert.Equal("COMBO", error.ParameterName);
        Assert.Equal("COMBO_TAKEN", error.ErrorCode);
    }

    [Fact]
    public async Task MustAsync_ShouldPassActualPropertyValue_ToPredicate()
    {
        var user = new User { Email = "hello@world.com" };
        string? observed = null;

        await Validate.For(user)
            .MustAsync(u => u.Email, (email, _) =>
            {
                observed = email;
                return Async(true);
            }, "bad")
            .ToResultAsync();

        Assert.Equal("hello@world.com", observed);
    }

    #endregion

    #region Cancellation

    [Fact]
    public async Task ToResultAsync_ShouldHonorCancellation_BeforeRuleRuns()
    {
        var user = new User { Email = "x@example.com" };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var ranRule = false;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await Validate.For(user)
                .MustAsync(u => u.Email, (email, _) =>
                {
                    ranRule = true;
                    return Async(true);
                }, "bad")
                .ToResultAsync(cts.Token));

        Assert.False(ranRule);
    }

    [Fact]
    public async Task ToResultAsync_ShouldPropagateCancellation_FromInsideRule()
    {
        var user = new User { Email = "x@example.com" };
        using var cts = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await Validate.For(user)
                .MustAsync(u => u.Email, async (email, ct) =>
                {
                    cts.Cancel();
                    ct.ThrowIfCancellationRequested();
                    return await Async(true);
                }, "bad")
                .ToResultAsync(cts.Token));
    }

    [Fact]
    public async Task ToResultAsync_ShouldNotConvertCancellation_IntoValidationError()
    {
        var user = new User { Email = "x@example.com" };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var validator = Validate.For(user)
            .MustAsync(u => u.Email, (email, _) => Async(false), "bad");

        // Cancellation must surface as OperationCanceledException, never be swallowed into a result.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await validator.ToResultAsync(cts.Token));
    }

    #endregion

    #region Mixed Sync + Async Aggregation

    [Fact]
    public async Task ToResultAsync_ShouldAggregate_SyncAndAsyncFailures()
    {
        var user = new User { Email = "taken@example.com", Username = null, Age = -1 };

        var result = await Validate.For(user)
            .NotEmpty(u => u.Username)                                  // sync failure
            .Must(u => u.Age, age => age > 0, "Age must be positive")  // sync failure
            .MustAsync(u => u.Email, (email, _) => Async(false), "Email already in use", "EMAIL_TAKEN")
            .ToResultAsync();

        Assert.True(result.IsInvalid);
        Assert.Equal(3, result.Errors.Count);
        Assert.Contains(result.Errors, e => e.ParameterName == "Username");
        Assert.Contains(result.Errors, e => e.Message == "Age must be positive");
        Assert.Contains(result.Errors, e => e.ErrorCode == "EMAIL_TAKEN");
    }

    [Fact]
    public async Task ToResultAsync_ShouldSurfaceSyncErrorsFirst_ThenAsync()
    {
        var user = new User { Email = "taken@example.com", Username = null };

        var result = await Validate.For(user)
            .NotEmpty(u => u.Username)
            .MustAsync(u => u.Email, (email, _) => Async(false), "Email already in use", "EMAIL_TAKEN")
            .ToResultAsync();

        Assert.Equal(2, result.Errors.Count);
        Assert.Equal("Username", result.Errors[0].ParameterName);
        Assert.Equal("EMAIL_TAKEN", result.Errors[1].ErrorCode);
    }

    [Fact]
    public async Task ToResultAsync_ShouldAggregate_MultipleAsyncFailures()
    {
        var user = new User { Email = "taken@example.com", Username = "taken" };

        var result = await Validate.For(user)
            .MustAsync(u => u.Email, (email, _) => Async(false), "Email taken", "EMAIL")
            .MustAsync(u => u.Username, (name, _) => Async(false), "Username taken", "USERNAME")
            .ToResultAsync();

        Assert.Equal(2, result.Errors.Count);
        Assert.Contains(result.Errors, e => e.ErrorCode == "EMAIL");
        Assert.Contains(result.Errors, e => e.ErrorCode == "USERNAME");
    }

    [Fact]
    public async Task ToResultAsync_ShouldReturnSuccess_WhenSyncAndAsyncAllPass()
    {
        var user = new User { Email = "free@example.com", Username = "available", Age = 30 };

        var result = await Validate.For(user)
            .NotEmpty(u => u.Username)
            .Must(u => u.Age, age => age > 0, "Age must be positive")
            .MustAsync(u => u.Email, (email, _) => Async(true), "Email taken")
            .MustAsync(u => u.Username, (name, _) => Async(true), "Username taken")
            .ToResultAsync();

        Assert.True(result.IsValid);
    }

    #endregion

    #region Short-Circuit Parity With Sync Strict Path

    [Fact]
    public async Task ToResultAsync_Strict_ShouldThrow_OnFirstAsyncFailure()
    {
        var user = new User { Email = "taken@example.com", Username = "taken" };

        await Assert.ThrowsAsync<AggregateValidationException>(async () =>
            await Validate.ForStrict(user)
                .MustAsync(u => u.Email, (email, _) => Async(false), "Email taken", "EMAIL")
                .MustAsync(u => u.Username, (name, _) => Async(false), "Username taken", "USERNAME")
                .ToResultAsync());
    }

    [Fact]
    public async Task ToResultAsync_Strict_ShouldNotRunRulesAfterFirstFailure()
    {
        var user = new User { Email = "taken@example.com", Username = "taken" };
        var secondRuleRan = false;

        await Assert.ThrowsAsync<AggregateValidationException>(async () =>
            await Validate.ForStrict(user)
                .MustAsync(u => u.Email, (email, _) => Async(false), "Email taken", "EMAIL")
                .MustAsync(u => u.Username, (name, _) =>
                {
                    secondRuleRan = true;
                    return Async(false);
                }, "Username taken", "USERNAME")
                .ToResultAsync());

        // Strict short-circuit: the second async rule must never execute, matching the sync path.
        Assert.False(secondRuleRan);
    }

    [Fact]
    public async Task ToResultAsync_NonStrict_ShouldRunAllRules_AndAggregate()
    {
        var user = new User { Email = "taken@example.com", Username = "taken" };
        var secondRuleRan = false;

        var result = await Validate.For(user)
            .MustAsync(u => u.Email, (email, _) => Async(false), "Email taken", "EMAIL")
            .MustAsync(u => u.Username, (name, _) =>
            {
                secondRuleRan = true;
                return Async(false);
            }, "Username taken", "USERNAME")
            .ToResultAsync();

        Assert.True(secondRuleRan);
        Assert.Equal(2, result.Errors.Count);
    }

    [Fact]
    public async Task BuildAsync_Strict_ShouldReturnInstance_WhenAllRulesPass()
    {
        var user = new User { Email = "free@example.com" };

        var built = await Validate.ForStrict(user)
            .MustAsync(u => u.Email, (email, _) => Async(true), "Email taken")
            .BuildAsync();

        Assert.Same(user, built);
    }

    #endregion

    #region Async Terminals

    [Fact]
    public async Task ThrowIfInvalidAsync_ShouldThrow_WhenInvalid()
    {
        var user = new User { Email = "taken@example.com" };

        await Assert.ThrowsAsync<AggregateValidationException>(async () =>
            await Validate.For(user)
                .MustAsync(u => u.Email, (email, _) => Async(false), "Email taken")
                .ThrowIfInvalidAsync());
    }

    [Fact]
    public async Task ThrowIfInvalidAsync_ShouldReturnInstance_WhenValid()
    {
        var user = new User { Email = "free@example.com" };

        var returned = await Validate.For(user)
            .MustAsync(u => u.Email, (email, _) => Async(true), "Email taken")
            .ThrowIfInvalidAsync();

        Assert.Same(user, returned);
    }

    [Fact]
    public async Task ToResultAsync_ShouldSucceed_WhenNoAsyncRulesRegistered()
    {
        var user = new User { Username = "ok", Age = 20 };

        var result = await Validate.For(user)
            .NotEmpty(u => u.Username)
            .Must(u => u.Age, age => age > 0, "Age must be positive")
            .ToResultAsync();

        Assert.True(result.IsValid);
    }

    #endregion

    #region Sync Terminal Guard When Async Rules Pending

    [Fact]
    public void ToResult_ShouldThrow_WhenAsyncRulesPending()
    {
        var user = new User { Email = "x@example.com" };

        var validator = Validate.For(user)
            .MustAsync(u => u.Email, (email, _) => Async(true), "bad");

        Assert.Throws<InvalidOperationException>(() => validator.ToResult());
    }

    [Fact]
    public void ThrowIfInvalid_ShouldThrow_WhenAsyncRulesPending()
    {
        var user = new User { Email = "x@example.com" };

        var validator = Validate.For(user)
            .MustAsync(u => u.Email, (email, _) => Async(true), "bad");

        Assert.Throws<InvalidOperationException>(() => validator.ThrowIfInvalid());
    }

    [Fact]
    public void Build_ShouldThrow_WhenAsyncRulesPending()
    {
        var user = new User { Email = "x@example.com" };

        var validator = Validate.For(user)
            .MustAsync(u => u.Email, (email, _) => Async(true), "bad");

        Assert.Throws<InvalidOperationException>(() => validator.Build());
    }

    #endregion

    #region Idempotent Async Terminal

    [Fact]
    public async Task ToResultAsync_CalledTwice_ShouldYieldSameErrors_AndRunPredicateOnce()
    {
        var user = new User { Email = "taken@example.com" };
        var invocations = 0;

        var validator = Validate.For(user)
            .MustAsync(u => u.Email, (email, _) =>
            {
                invocations++;
                return Async(false);
            }, "Email already in use", "EMAIL_TAKEN");

        var first = await validator.ToResultAsync();
        var second = await validator.ToResultAsync();

        // Same single error set on both calls -- no accumulation across repeated terminal calls.
        var e1 = Assert.Single(first.Errors);
        var e2 = Assert.Single(second.Errors);
        Assert.Equal("EMAIL_TAKEN", e1.ErrorCode);
        Assert.Equal("EMAIL_TAKEN", e2.ErrorCode);

        // The async predicate (and any I/O side effect inside it) runs exactly once.
        Assert.Equal(1, invocations);
    }

    [Fact]
    public async Task ThrowIfInvalidAsync_AfterToResultAsync_ShouldStaySingleError_AndNotRerunPredicate()
    {
        var user = new User { Email = "taken@example.com" };
        var invocations = 0;

        var validator = Validate.For(user)
            .MustAsync(u => u.Email, (email, _) =>
            {
                invocations++;
                return Async(false);
            }, "Email already in use", "EMAIL_TAKEN");

        var result = await validator.ToResultAsync();
        Assert.Single(result.Errors);

        var ex = await Assert.ThrowsAsync<AggregateValidationException>(
            () => validator.ThrowIfInvalidAsync());

        // The exception carries the SAME single error, not duplicates, and the predicate is not re-run.
        Assert.Single(ex.Errors);
        Assert.Equal(1, invocations);
    }

    [Fact]
    public async Task ToResultAsync_CalledTwice_ShouldAggregateSyncAndAsync_WithoutDuplicating()
    {
        var user = new User { Email = "taken@example.com", Username = null };
        var invocations = 0;

        var validator = Validate.For(user)
            .NotEmpty(u => u.Username)
            .MustAsync(u => u.Email, (email, _) =>
            {
                invocations++;
                return Async(false);
            }, "Email already in use", "EMAIL_TAKEN");

        var first = await validator.ToResultAsync();
        var second = await validator.ToResultAsync();

        Assert.Equal(2, first.Errors.Count);
        Assert.Equal(2, second.Errors.Count);
        Assert.Equal("Username", second.Errors[0].ParameterName);
        Assert.Equal("EMAIL_TAKEN", second.Errors[1].ErrorCode);
        Assert.Equal(1, invocations);
    }

    #endregion

    #region WhenAsync Conditional

    [Fact]
    public async Task WhenAsync_ShouldRegisterRules_WhenConditionTrue()
    {
        var user = new User { Email = "taken@example.com" };

        var result = await Validate.For(user)
            .WhenAsync(true, v => v.MustAsync(u => u.Email, (email, _) => Async(false), "Email taken"))
            .ToResultAsync();

        Assert.True(result.IsInvalid);
    }

    [Fact]
    public async Task WhenAsync_ShouldSkipRules_WhenConditionFalse()
    {
        var user = new User { Email = "taken@example.com" };

        var result = await Validate.For(user)
            .WhenAsync(false, v => v.MustAsync(u => u.Email, (email, _) => Async(false), "Email taken"))
            .ToResultAsync();

        Assert.True(result.IsValid);
    }

    #endregion

    #region Argument Validation

    [Fact]
    public void MustAsync_ShouldThrow_WhenSelectorIsNull()
    {
        var user = new User();
        Assert.Throws<ArgumentNullException>(() =>
            Validate.For(user).MustAsync<string?>(null!, (e, _) => Async(true), "bad"));
    }

    [Fact]
    public void MustAsync_ShouldThrow_WhenPredicateIsNull()
    {
        var user = new User();
        Assert.Throws<ArgumentNullException>(() =>
            Validate.For(user).MustAsync(u => u.Email, null!, "bad"));
    }

    #endregion
}
