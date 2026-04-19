using Moongazing.OrionGuard.Core;

namespace Moongazing.OrionGuard.Tests;

public class CrossPropertyValidatorTests
{
    #region Test DTOs

    private sealed class UserForm
    {
        public string? Password { get; set; }
        public string? ConfirmPassword { get; set; }
        public string? Email { get; set; }
        public string? Username { get; set; }
        public string? Phone { get; set; }
    }

    private sealed class DateRange
    {
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
    }

    private sealed class NumberPair
    {
        public int Min { get; set; }
        public int Max { get; set; }
    }

    private sealed class ConditionalForm
    {
        public bool RequiresApproval { get; set; }
        public string? ApproverName { get; set; }
        public string? ApproverEmail { get; set; }
    }

    #endregion

    #region AreEqual

    [Fact]
    public void AreEqual_ShouldPass_WhenValuesMatch()
    {
        var form = new UserForm { Password = "Secret123", ConfirmPassword = "Secret123" };

        var result = Validate.CrossProperties(form)
            .AreEqual(u => u.Password, u => u.ConfirmPassword)
            .ToResult();

        Assert.True(result.IsValid);
    }

    [Fact]
    public void AreEqual_ShouldFail_WhenValuesDiffer()
    {
        var form = new UserForm { Password = "Secret123", ConfirmPassword = "Different" };

        var result = Validate.CrossProperties(form)
            .AreEqual(u => u.Password, u => u.ConfirmPassword)
            .ToResult();

        Assert.True(result.IsInvalid);
        Assert.Equal("CROSS_EQUAL", result.Errors[0].ErrorCode);
    }

    [Fact]
    public void AreEqual_ShouldPass_WhenBothNull()
    {
        var form = new UserForm { Password = null, ConfirmPassword = null };

        var result = Validate.CrossProperties(form)
            .AreEqual(u => u.Password, u => u.ConfirmPassword)
            .ToResult();

        Assert.True(result.IsValid);
    }

    #endregion

    #region AreNotEqual

    [Fact]
    public void AreNotEqual_ShouldPass_WhenValuesDiffer()
    {
        var form = new UserForm { Email = "alice@example.com", Username = "alice123" };

        var result = Validate.CrossProperties(form)
            .AreNotEqual(u => u.Email, u => u.Username)
            .ToResult();

        Assert.True(result.IsValid);
    }

    [Fact]
    public void AreNotEqual_ShouldFail_WhenValuesSame()
    {
        var form = new UserForm { Email = "alice", Username = "alice" };

        var result = Validate.CrossProperties(form)
            .AreNotEqual(u => u.Email, u => u.Username)
            .ToResult();

        Assert.True(result.IsInvalid);
        Assert.Equal("CROSS_NOT_EQUAL", result.Errors[0].ErrorCode);
    }

    #endregion

    #region IsGreaterThan

    [Fact]
    public void IsGreaterThan_ShouldPass_WhenLeftGreaterThanRight_Dates()
    {
        var range = new DateRange
        {
            StartDate = new DateTime(2025, 1, 1),
            EndDate = new DateTime(2025, 12, 31)
        };

        var result = Validate.CrossProperties(range)
            .IsGreaterThan(r => r.EndDate, r => r.StartDate)
            .ToResult();

        Assert.True(result.IsValid);
    }

    [Fact]
    public void IsGreaterThan_ShouldFail_WhenLeftNotGreaterThanRight_Dates()
    {
        var range = new DateRange
        {
            StartDate = new DateTime(2025, 12, 31),
            EndDate = new DateTime(2025, 1, 1)
        };

        var result = Validate.CrossProperties(range)
            .IsGreaterThan(r => r.EndDate, r => r.StartDate)
            .ToResult();

        Assert.True(result.IsInvalid);
        Assert.Equal("CROSS_GREATER_THAN", result.Errors[0].ErrorCode);
    }

    [Fact]
    public void IsGreaterThan_ShouldFail_WhenEqual_Dates()
    {
        var date = new DateTime(2025, 6, 15);
        var range = new DateRange { StartDate = date, EndDate = date };

        var result = Validate.CrossProperties(range)
            .IsGreaterThan(r => r.EndDate, r => r.StartDate)
            .ToResult();

        Assert.True(result.IsInvalid);
    }

    [Fact]
    public void IsGreaterThan_ShouldPass_WhenLeftGreaterThanRight_Numbers()
    {
        var pair = new NumberPair { Min = 1, Max = 100 };

        var result = Validate.CrossProperties(pair)
            .IsGreaterThan(p => p.Max, p => p.Min)
            .ToResult();

        Assert.True(result.IsValid);
    }

    [Fact]
    public void IsGreaterThan_ShouldFail_WhenLeftLessThanRight_Numbers()
    {
        var pair = new NumberPair { Min = 100, Max = 1 };

        var result = Validate.CrossProperties(pair)
            .IsGreaterThan(p => p.Max, p => p.Min)
            .ToResult();

        Assert.True(result.IsInvalid);
    }

    #endregion

    #region AtLeastOneRequired

    [Fact]
    public void AtLeastOneRequired_ShouldPass_WhenFirstHasValue()
    {
        var form = new UserForm { Email = "alice@example.com", Phone = null };

        var result = Validate.CrossProperties(form)
            .AtLeastOneRequired(u => u.Email, u => u.Phone)
            .ToResult();

        Assert.True(result.IsValid);
    }

    [Fact]
    public void AtLeastOneRequired_ShouldPass_WhenSecondHasValue()
    {
        var form = new UserForm { Email = null, Phone = "+1234567890" };

        var result = Validate.CrossProperties(form)
            .AtLeastOneRequired(u => u.Email, u => u.Phone)
            .ToResult();

        Assert.True(result.IsValid);
    }

    [Fact]
    public void AtLeastOneRequired_ShouldPass_WhenBothHaveValues()
    {
        var form = new UserForm { Email = "alice@example.com", Phone = "+1234567890" };

        var result = Validate.CrossProperties(form)
            .AtLeastOneRequired(u => u.Email, u => u.Phone)
            .ToResult();

        Assert.True(result.IsValid);
    }

    [Fact]
    public void AtLeastOneRequired_ShouldFail_WhenBothEmpty()
    {
        var form = new UserForm { Email = null, Phone = null };

        var result = Validate.CrossProperties(form)
            .AtLeastOneRequired(u => u.Email, u => u.Phone)
            .ToResult();

        Assert.True(result.IsInvalid);
        Assert.Equal("AT_LEAST_ONE", result.Errors[0].ErrorCode);
    }

    [Fact]
    public void AtLeastOneRequired_ShouldFail_WhenBothWhitespace()
    {
        var form = new UserForm { Email = "   ", Phone = "  " };

        var result = Validate.CrossProperties(form)
            .AtLeastOneRequired(u => u.Email, u => u.Phone)
            .ToResult();

        Assert.True(result.IsInvalid);
    }

    #endregion

    #region When Conditional

    [Fact]
    public void When_ShouldApplyRules_WhenConditionTrue()
    {
        var form = new ConditionalForm
        {
            RequiresApproval = true,
            ApproverName = null,
            ApproverEmail = null
        };

        var result = Validate.CrossProperties(form)
            .When(f => f.RequiresApproval, v => v
                .AtLeastOneRequired(f => f.ApproverName, f => f.ApproverEmail))
            .ToResult();

        Assert.True(result.IsInvalid);
    }

    [Fact]
    public void When_ShouldSkipRules_WhenConditionFalse()
    {
        var form = new ConditionalForm
        {
            RequiresApproval = false,
            ApproverName = null,
            ApproverEmail = null
        };

        var result = Validate.CrossProperties(form)
            .When(f => f.RequiresApproval, v => v
                .AtLeastOneRequired(f => f.ApproverName, f => f.ApproverEmail))
            .ToResult();

        Assert.True(result.IsValid);
    }

    #endregion

    #region ThrowIfInvalid

    [Fact]
    public void ThrowIfInvalid_ShouldThrow_WhenErrors()
    {
        var form = new UserForm { Password = "abc", ConfirmPassword = "xyz" };

        Assert.Throws<AggregateValidationException>(() =>
            Validate.CrossProperties(form)
                .AreEqual(u => u.Password, u => u.ConfirmPassword)
                .ThrowIfInvalid());
    }

    #endregion
}
