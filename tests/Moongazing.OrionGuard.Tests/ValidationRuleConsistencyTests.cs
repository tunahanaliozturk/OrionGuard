using System.Diagnostics;
using Moongazing.OrionGuard.Attributes;
using Moongazing.OrionGuard.Core;
using Moongazing.OrionGuard.DynamicRules;
using Moongazing.OrionGuard.Exceptions;
using Moongazing.OrionGuard.Extensions;
using Moongazing.OrionGuard.Profiles;

namespace Moongazing.OrionGuard.Tests;

/// <summary>
/// Every email and URL check in the library must give the same answer for the same input, whether it is a
/// throwing guard, a span guard, an accumulating guard, an attribute, a profile or a dynamic rule.
/// </summary>
public class ValidationRuleConsistencyTests
{
    private static readonly DynamicValidator DynamicEmailAndUrl = DynamicValidator.FromJson(
        """{"Name":"r","Rules":[{"PropertyName":"Email","RuleType":"Email"},{"PropertyName":"Site","RuleType":"Url"}]}""");

    private static readonly string LongestValidEmail =
        new string('a', 64) + "@" + new string('b', 63) + "." + new string('c', 63) + "." + new string('d', 61);

    private sealed record Contact(string? Email, string? Site);

    #region Email

    public static TheoryData<string> InvalidEmails => new()
    {
        "a@b@c.com",
        "a@b.co\n",
        "a@.b.com",
        "a@b..com",
        "a@b.com.",
        "a@b\t.com",
        LongestValidEmail + "x",
    };

    [Theory]
    [MemberData(nameof(InvalidEmails))]
    public void EveryEmailCheck_ShouldReject_WhenAddressIsMalformed(string email)
    {
        Assert.Throws<InvalidEmailException>(() => Guard.AgainstInvalidEmail(email, "e"));
        Assert.Throws<InvalidEmailException>(() => email.AgainstInvalidEmail("e"));
        Assert.Throws<ArgumentException>(() => FastGuard.Email(email, "e"));
        Assert.Throws<RegexMismatchException>(() => GuardProfiles.Email(email, "e"));
        Assert.False(Ensure.Accumulate(email, "e").Email().ToResult().IsValid);
        Assert.False(CommonProfiles.Email(email).IsValid);
        Assert.False(new EmailAttribute().IsValid(email));
        Assert.False(DynamicEmailAndUrl.Validate(new Contact(email, null)).IsValid);
    }

    public static TheoryData<string> ValidEmails => new()
    {
        "user@example.com",
        "first.last+tag@sub.example.co.uk",
        "o'brien@example.ie",
        "x@y.io",
        LongestValidEmail,
    };

    [Theory]
    [MemberData(nameof(ValidEmails))]
    public void EveryEmailCheck_ShouldAccept_WhenAddressIsOrdinary(string email)
    {
        Assert.Null(Record.Exception(() => Guard.AgainstInvalidEmail(email, "e")));
        Assert.Null(Record.Exception(() => email.AgainstInvalidEmail("e")));
        Assert.Null(Record.Exception(() => FastGuard.Email(email, "e")));
        Assert.Null(Record.Exception(() => GuardProfiles.Email(email, "e")));
        Assert.True(Ensure.Accumulate(email, "e").Email().ToResult().IsValid);
        Assert.True(CommonProfiles.Email(email).IsValid);
        Assert.True(new EmailAttribute().IsValid(email));
        Assert.True(DynamicEmailAndUrl.Validate(new Contact(email, null)).IsValid);
    }

    [Fact]
    public void EnsureAccumulateEmail_ShouldReportErrorQuickly_WhenInputIsLongBacktrackingPayload()
    {
        // "a@a.a.a. ... a. " made the old domain pattern backtrack quadratically until the 1 s timeout,
        // and the RegexMatchTimeoutException escaped the accumulating API as an HTTP 500.
        var hostile = "a@" + string.Concat(Enumerable.Repeat("a.", 100_000)) + " ";
        var stopwatch = Stopwatch.StartNew();

        var result = Ensure.Accumulate(hostile, "e").Email().ToResult();

        Assert.False(result.IsValid);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1), $"Email check took {stopwatch.Elapsed}.");
    }

    #endregion

    #region Url

    [Theory]
    [InlineData("javascript:alert(document.cookie)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://example.com/file.txt")]
    public void EveryUrlCheck_ShouldReject_WhenSchemeIsNotHttp(string url)
    {
        Assert.False(Ensure.Accumulate(url, "u").Url().ToResult().IsValid);
        Assert.False(CommonProfiles.Url(url).IsValid);
        Assert.False(DynamicEmailAndUrl.Validate(new Contact(null, url)).IsValid);
        Assert.Throws<InvalidUrlException>(() => Guard.AgainstInvalidUrl(url, "u"));
        Assert.Throws<ArgumentException>(() => url.AgainstInvalidUrl("u"));
    }

    [Theory]
    [InlineData("https://ok.example")]
    [InlineData("http://localhost:5000/path?q=1")]
    public void EveryUrlCheck_ShouldAccept_WhenUrlIsAbsoluteHttp(string url)
    {
        Assert.True(Ensure.Accumulate(url, "u").Url().ToResult().IsValid);
        Assert.True(CommonProfiles.Url(url).IsValid);
        Assert.True(DynamicEmailAndUrl.Validate(new Contact(null, url)).IsValid);
        Assert.Null(Record.Exception(() => Guard.AgainstInvalidUrl(url, "u")));
    }

    #endregion
}
