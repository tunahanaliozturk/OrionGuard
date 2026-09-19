using Moongazing.OrionGuard.Core;
using Moongazing.OrionGuard.Exceptions;
using Moongazing.OrionGuard.Extensions;

namespace Moongazing.OrionGuard.Tests;

/// <summary>
/// BUG-C8: date guards compared raw ticks with <see cref="DateTime.UtcNow"/>, so a
/// <see cref="DateTimeKind.Local"/> value was off by the machine's UTC offset.
/// </summary>
/// <remarks>
/// Every value is built from <see cref="DateTime.UtcNow"/> with an explicit Kind, so the expected outcome
/// is the same in any time zone. Local values are real instants (<c>ToLocalTime()</c> of a UTC instant),
/// which a correct guard must place on the UTC timeline. The old code only failed where the local offset
/// is non-zero: the "ago" cases failed east of UTC, the "ahead" cases west of UTC.
/// </remarks>
public class DateTimeKindTests
{
    private static DateTime LocalInstant(TimeSpan fromNow) => DateTime.UtcNow.Add(fromNow).ToLocalTime();

    private static DateTime UnspecifiedInstant(TimeSpan fromNow) =>
        DateTime.SpecifyKind(DateTime.UtcNow.Add(fromNow), DateTimeKind.Unspecified);

    #region FluentGuard

    [Fact]
    public void InPast_ShouldPass_WhenLocalValueIsOneMinuteAgo()
    {
        var result = Ensure.Accumulate(LocalInstant(TimeSpan.FromMinutes(-1)), "d").InPast().ToResult();

        Assert.True(result.IsValid);
    }

    [Fact]
    public void InFuture_ShouldPass_WhenLocalValueIsOneMinuteAhead()
    {
        var result = Ensure.Accumulate(LocalInstant(TimeSpan.FromMinutes(1)), "d").InFuture().ToResult();

        Assert.True(result.IsValid);
    }

    [Fact]
    public void InFuture_ShouldFail_WhenLocalValueIsOneHourAgo()
    {
        var result = Ensure.Accumulate(LocalInstant(TimeSpan.FromHours(-1)), "d").InFuture().ToResult();

        Assert.True(result.IsInvalid);
    }

    [Fact]
    public void InPast_ShouldPass_WhenUnspecifiedValueIsOneMinuteAgo()
    {
        // Unspecified is treated as UTC.
        Assert.True(Ensure.Accumulate(UnspecifiedInstant(TimeSpan.FromMinutes(-1)), "d").InPast().ToResult().IsValid);
    }

    #endregion

    #region Guard

    [Fact]
    public void AgainstFutureDate_ShouldNotThrow_WhenValueIsDateTimeNow()
    {
        Guard.AgainstFutureDate(DateTime.Now, "d");
    }

    [Fact]
    public void AgainstPastDate_ShouldThrow_WhenLocalValueIsTwoHoursAgo()
    {
        Assert.Throws<PastDateException>(() => Guard.AgainstPastDate(LocalInstant(TimeSpan.FromHours(-2)), "d"));
    }

    [Fact]
    public void AgainstPastDate_ShouldNotThrow_WhenLocalValueIsTwoHoursAhead()
    {
        Guard.AgainstPastDate(LocalInstant(TimeSpan.FromHours(2)), "d");
    }

    [Fact]
    public void AgainstFutureDate_ShouldNotThrow_WhenUnspecifiedValueIsOneMinuteAgo()
    {
        // Unspecified is treated as UTC.
        Guard.AgainstFutureDate(UnspecifiedInstant(TimeSpan.FromMinutes(-1)), "d");
    }

    [Fact]
    public void AgainstUnrealisticBirthDate_ShouldNotThrow_WhenLocalValueIsOneMinuteAgo()
    {
        Guard.AgainstUnrealisticBirthDate(LocalInstant(TimeSpan.FromMinutes(-1)), "d");
    }

    #endregion

    #region DateTimeGuards and BusinessGuards

    [Fact]
    public void DateTimeGuardsAgainstFutureDate_ShouldNotThrow_WhenLocalValueIsOneMinuteAgo()
    {
        LocalInstant(TimeSpan.FromMinutes(-1)).AgainstFutureDate("d");
    }

    [Fact]
    public void DateTimeGuardsAgainstPastDate_ShouldNotThrow_WhenLocalValueIsOneMinuteAhead()
    {
        LocalInstant(TimeSpan.FromMinutes(1)).AgainstPastDate("d");
    }

    [Fact]
    public void DateTimeGuardsAgainstFuturePeriod_ShouldNotThrow_WhenLocalValueIsWithinPeriod()
    {
        LocalInstant(TimeSpan.FromMinutes(30)).AgainstFuturePeriod(TimeSpan.FromHours(1), "d");
    }

    [Fact]
    public void AgainstExpired_ShouldNotThrow_WhenLocalExpirationIsThirtyMinutesAhead()
    {
        LocalInstant(TimeSpan.FromMinutes(30)).AgainstExpired("token");
    }

    [Fact]
    public void AgainstNotYetActive_ShouldNotThrow_WhenLocalActivationWasThirtyMinutesAgo()
    {
        LocalInstant(TimeSpan.FromMinutes(-30)).AgainstNotYetActive("token");
    }

    [Fact]
    public void AgainstExpired_ShouldThrow_WhenLocalExpirationWasThirtyMinutesAgo()
    {
        Assert.Throws<ArgumentException>(() => LocalInstant(TimeSpan.FromMinutes(-30)).AgainstExpired("token"));
    }

    #endregion
}
