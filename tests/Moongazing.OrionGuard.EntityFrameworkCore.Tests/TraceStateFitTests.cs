namespace Moongazing.OrionGuard.EntityFrameworkCore.Tests;

public class TraceStateFitTests
{
    [Theory]
    [InlineData(null, 256, null)]
    [InlineData("", 256, "")]
    [InlineData("a=1,b=2", 256, "a=1,b=2")]              // fits: unchanged
    [InlineData("a=1,b=2", null, "a=1,b=2")]             // unbounded column: unchanged
    [InlineData("a=1,b=2,c=3", 7, "a=1,b=2")]            // cut at a member boundary, never mid-member
    [InlineData("a=1, b=2 ,c=3", 7, "a=1,b=2")]          // optional whitespace around commas is dropped
    [InlineData("abcdefgh=1,b=2", 5, null)]              // the first member alone is too long: nothing fits
    public void FitTraceState_KeepsWholeLeadingMembersThatFit(string? traceState, int? maxLength, string? expected)
        => Assert.Equal(expected, DomainEventSaveChangesInterceptor.FitTraceState(traceState, maxLength));

    [Fact]
    public void FitTraceState_DropsMembersLongerThan128CharactersFirst()
    {
        // W3C trace-context: when tracestate must be truncated, oversized members go before any others.
        var oversized = "big=" + new string('x', 130);
        var traceState = $"a=1,{oversized},b=2," + string.Join(",", Enumerable.Range(0, 60).Select(i => $"k{i:D2}=v"));

        var fitted = DomainEventSaveChangesInterceptor.FitTraceState(traceState, 256);

        Assert.NotNull(fitted);
        Assert.DoesNotContain("big=", fitted);
        Assert.StartsWith("a=1,b=2,k00=v", fitted);
        Assert.True(fitted!.Length <= 256);
    }
}
