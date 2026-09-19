using Moongazing.OrionGuard.Core;
using Moongazing.OrionGuard.Exceptions;
using Moongazing.OrionGuard.Extensions;

namespace Moongazing.OrionGuard.Tests;

public class StringGuardsTests
{
    #region AgainstCharactersOutsideSet

    [Theory]
    [InlineData("123", "+-=")]   // '-' between '+' and '=' used to form the range U+002B..U+003D
    [InlineData("abc\n", "abc")] // '$' matched before a trailing newline
    public void AgainstCharactersOutsideSet_ShouldThrow_WhenValueHasCharacterOutsideLiteralSet(string value, string allowed)
    {
        Assert.Throws<ArgumentException>(() => value.AgainstCharactersOutsideSet(allowed, "v"));
        Assert.Throws<CharactersOutsideSetException>(() => Guard.AgainstCharactersOutsideSet(value, allowed, "v"));
    }

    [Fact]
    public void AgainstCharactersOutsideSet_ShouldStillThrow_WhenValueIsEmpty()
    {
        Assert.Throws<ArgumentException>(() => "".AgainstCharactersOutsideSet("abc", "v"));
        Assert.Throws<CharactersOutsideSetException>(() => Guard.AgainstCharactersOutsideSet("", "abc", "v"));
    }

    [Theory]
    [InlineData("a", "ab]")]    // ']' used to close the class early
    [InlineData("a", "z-a")]    // "z-a" used to throw RegexParseException (reversed range)
    [InlineData("-]^", "^]-")]
    [InlineData("aabbcc", "abc")]
    public void AgainstCharactersOutsideSet_ShouldNotThrow_WhenEveryCharacterIsInSet(string value, string allowed)
    {
        Assert.Null(Record.Exception(() => value.AgainstCharactersOutsideSet(allowed, "v")));
        Assert.Null(Record.Exception(() => Guard.AgainstCharactersOutsideSet(value, allowed, "v")));
    }

    #endregion

    #region AgainstContainingWhitespace

    [Theory]
    [InlineData("a\tb")]
    [InlineData("a\nb")]
    [InlineData("a b")] // no-break space
    [InlineData("a　b")] // ideographic space
    public void AgainstContainingWhitespace_ShouldThrow_WhenValueHasNonSpaceWhitespace(string value)
    {
        Assert.Throws<ArgumentException>(() => value.AgainstContainingWhitespace("v"));
    }

    [Fact]
    public void AgainstContainingWhitespace_ShouldNotThrow_WhenValueHasNoWhitespace()
    {
        Assert.Null(Record.Exception(() => "no_whitespace-here".AgainstContainingWhitespace("v")));
    }

    #endregion

    #region Anchored formats reject a trailing newline

    [Fact]
    public void AgainstNonAlphanumericCharacters_ShouldThrow_WhenValueEndsWithNewline()
    {
        Assert.Throws<ArgumentException>(() => "abc123\n".AgainstNonAlphanumericCharacters("v"));
        Assert.Throws<OnlyAlphanumericCharacterException>(() => Guard.AgainstNonAlphanumericCharacters("abc\n", "v"));
    }

    [Theory]
    [InlineData("42\n")]
    [InlineData("١٢٣")] // Arabic-Indic digits
    public void AgainstNonNumericCharacters_ShouldThrow_WhenValueIsNotOnlyAsciiDigits(string value)
    {
        Assert.Throws<ArgumentException>(() => value.AgainstNonNumericCharacters("v"));
    }

    [Theory]
    [InlineData("my-post\n")]
    public void AgainstInvalidSlug_ShouldThrow_WhenValueEndsWithNewline(string value)
    {
        Assert.Throws<ArgumentException>(() => value.AgainstInvalidSlug("v"));
    }

    [Fact]
    public void AgainstInvalidEmail_ShouldThrow_WhenValueEndsWithNewline()
    {
        Assert.Throws<InvalidEmailException>(() => "victim@example.com\n".AgainstInvalidEmail("e"));
        Assert.Throws<InvalidEmailException>(() => Guard.AgainstInvalidEmail("a@b.co\n", "e"));
    }

    [Fact]
    public void AnchoredFormats_ShouldNotThrow_WhenValueIsValid()
    {
        Assert.Null(Record.Exception(() => "abc123".AgainstNonAlphanumericCharacters("v")));
        Assert.Null(Record.Exception(() => "0123456789".AgainstNonNumericCharacters("v")));
        Assert.Null(Record.Exception(() => "my-post".AgainstInvalidSlug("v")));
        Assert.Null(Record.Exception(() => "1.2.3-rc.1+build.5".AgainstInvalidSemVer("v")));
    }

    #endregion
}
