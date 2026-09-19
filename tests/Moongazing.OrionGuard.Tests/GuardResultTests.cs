using Moongazing.OrionGuard.Core;

namespace Moongazing.OrionGuard.Tests;

public class GuardResultTests
{
    [Fact]
    public void Merge_ShouldKeepSuggestedHttpStatusCode_WhenOtherResultHasNone()
    {
        var merged = GuardResult.FailureWithStatus(409, "x", "conflict").Merge(GuardResult.Success());

        Assert.Equal(409, merged.SuggestedHttpStatusCode);
    }

    [Fact]
    public void Merge_ShouldTakeOtherSuggestedHttpStatusCode_WhenThisResultHasNone()
    {
        var merged = GuardResult.Success().Merge(GuardResult.FailureWithStatus(409, "x", "conflict"));

        Assert.Equal(409, merged.SuggestedHttpStatusCode);
    }

    [Fact]
    public void Combine_ShouldCarryFirstNonNullSuggestedHttpStatusCode_WhenSeveralResultsHaveOne()
    {
        var combined = GuardResult.Combine(
            GuardResult.Failure("a", "plain"),
            GuardResult.FailureWithStatus(404, "b", "missing"),
            GuardResult.FailureWithStatus(409, "c", "conflict"));

        Assert.Equal(404, combined.SuggestedHttpStatusCode);
        Assert.Equal(3, combined.Errors.Count);
    }

    [Fact]
    public void Combine_ShouldLeaveSuggestedHttpStatusCodeNull_WhenNoResultHasOne()
    {
        var combined = GuardResult.Combine(GuardResult.Failure("a", "plain"), GuardResult.Success());

        Assert.Null(combined.SuggestedHttpStatusCode);
        Assert.True(combined.IsInvalid);
    }

    [Fact]
    public void Combine_ShouldReturnValidResult_WhenAllResultsAreSuccessful()
    {
        var combined = GuardResult.Combine(GuardResult.Success(), GuardResult.Success());

        Assert.True(combined.IsValid);
        Assert.Null(combined.SuggestedHttpStatusCode);
    }

    [Fact]
    public void Success_ShouldCarryNoState_WhenFailuresWereProducedAroundIt()
    {
        var before = GuardResult.Success();

        var failed = GuardResult.Failure("a", "boom").Merge(GuardResult.FailureWithStatus(409, "b", "conflict"));
        Assert.Throws<AggregateValidationException>(failed.ThrowIfInvalid);
        GuardResult.Combine(GuardResult.FailureWithStatus(404, "c", "missing"), before);

        var after = GuardResult.Success();

        Assert.True(before.IsValid);
        Assert.True(after.IsValid);
        Assert.Empty(before.Errors);
        Assert.Empty(after.AllIssues);
        Assert.Null(before.SuggestedHttpStatusCode);
        Assert.Null(after.SuggestedHttpStatusCode);
        Assert.Equal(string.Empty, after.GetErrorSummary());
        Assert.Empty(after.ToErrorDictionary());
    }

    [Fact]
    public void Failure_ShouldNotSeeLaterAdditionsToTheSourceList_WhenTheCallerKeepsMutatingIt()
    {
        var source = new List<ValidationError> { new("a", "first") };

        var result = GuardResult.Failure(source);
        source.Add(new ValidationError("b", "second"));

        Assert.Single(result.Errors);
    }
}

