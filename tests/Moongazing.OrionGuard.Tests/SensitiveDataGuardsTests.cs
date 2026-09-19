using Moongazing.OrionGuard.Extensions;

namespace Moongazing.OrionGuard.Tests;

public class SensitiveDataGuardsTests
{
    #region AgainstContainsCreditCardNumber

    [Theory]
    [InlineData("4111 1111 1111 1111")]
    [InlineData("4111-1111-1111-1111")]
    [InlineData("Card: 3782 822463 10005 on file")]
    [InlineData("Ref 12 4111 1111 1111 1111 12/25")]
    [InlineData("2223003122003222")]
    [InlineData("2223 0031 2200 3222")]
    [InlineData("6200000000000005")]
    public void AgainstContainsCreditCardNumber_ShouldThrow_WhenCardIsSeparatedOrUsesNewerPrefix(string input)
    {
        Assert.Throws<ArgumentException>(() => input.AgainstContainsCreditCardNumber(nameof(input)));
    }

    // Detected before the group-aware rewrite of the scan as well.
    [Theory]
    [InlineData("4111111111111111")]
    [InlineData("pay with 5555555555554444 today")]
    public void AgainstContainsCreditCardNumber_ShouldStillThrow_WhenCardIsContiguous(string input)
    {
        Assert.Throws<ArgumentException>(() => input.AgainstContainsCreditCardNumber(nameof(input)));
    }

    [Theory]
    [InlineData("Order 12345 shipped on 2024-05-01")]
    [InlineData("Call 555-123-4567")]
    [InlineData("4111 1111 1111 1112")]
    [InlineData("Invoice 1234567890123456789012345")]
    public void AgainstContainsCreditCardNumber_ShouldNotThrow_WhenNoCardIsPresent(string input)
    {
        var exception = Record.Exception(() => input.AgainstContainsCreditCardNumber(nameof(input)));
        Assert.Null(exception);
    }

    #endregion

    #region AgainstContainsSecret

    [Fact]
    public void AgainstContainsSecret_ShouldThrow_WhenValueIsBareJwt()
    {
        var jwt = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.dozjgNryP4J3jVmNHl0w5N_XgL0n3I9PlFUP0THsR8U";

        Assert.Throws<ArgumentException>(() => jwt.AgainstContainsSecret(nameof(jwt)));
    }

    // Token-shaped test values are assembled at run time so that secret scanners (GitHub push protection)
    // do not mistake this file for a leaked credential.
    public static TheoryData<string> AccessTokens => new()
    {
        "token=" + "ghp_" + new string('a', 36),
        "gho_" + new string('B', 36),
        "ghs_" + new string('7', 36),
        "ghu_" + new string('c', 36),
        "github_pat_" + new string('D', 22) + "_" + new string('e', 59),
        "stripe: " + "sk_" + "live_" + new string('f', 24),
        "rk_" + "live_" + new string('G', 24),
    };

    [Theory]
    [MemberData(nameof(AccessTokens))]
    public void AgainstContainsSecret_ShouldThrow_WhenValueContainsAccessToken(string input)
    {
        Assert.Throws<ArgumentException>(() => input.AgainstContainsSecret(nameof(input)));
    }

    [Fact]
    public void AgainstContainsSecret_ShouldNotTimeOut_WhenInputRepeatsTokenPrefixes()
    {
        var input = string.Concat(Enumerable.Repeat("eyJ", 200_000)) + " " + string.Concat(Enumerable.Repeat("ghp_", 100_000));

        var exception = Record.Exception(() => input.AgainstContainsSecret(nameof(input)));

        Assert.Null(exception);
    }

    [Theory]
    [InlineData("User signed in")]
    [InlineData("ghp_short")]
    [InlineData("sk_test_is_not_a_live_key")]
    [InlineData("Version 1.2.3 released")]
    public void AgainstContainsSecret_ShouldNotThrow_WhenValueHasNoSecret(string input)
    {
        var exception = Record.Exception(() => input.AgainstContainsSecret(nameof(input)));
        Assert.Null(exception);
    }

    #endregion
}
