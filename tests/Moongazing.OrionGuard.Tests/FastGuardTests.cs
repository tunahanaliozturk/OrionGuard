using Moongazing.OrionGuard.Core;

namespace Moongazing.OrionGuard.Tests;

public class FastGuardTests
{
    #region NotNullOrEmpty

    [Fact]
    public void NotNullOrEmpty_ShouldThrow_WhenNull()
    {
        Assert.Throws<ArgumentException>(() => FastGuard.NotNullOrEmpty(null, "param"));
    }

    [Fact]
    public void NotNullOrEmpty_ShouldThrow_WhenEmpty()
    {
        Assert.Throws<ArgumentException>(() => FastGuard.NotNullOrEmpty("", "param"));
    }

    [Fact]
    public void NotNullOrEmpty_ShouldReturnValue_WhenValid()
    {
        var result = FastGuard.NotNullOrEmpty("hello", "param");
        Assert.Equal("hello", result);
    }

    #endregion

    #region NotEmpty (Span)

    [Fact]
    public void NotEmpty_ShouldThrow_WhenSpanIsEmpty()
    {
        Assert.Throws<ArgumentException>(() => FastGuard.NotEmpty(ReadOnlySpan<char>.Empty, "param"));
    }

    [Fact]
    public void NotEmpty_ShouldNotThrow_WhenSpanHasContent()
    {
        var exception = Record.Exception(() => FastGuard.NotEmpty("hello".AsSpan(), "param"));
        Assert.Null(exception);
    }

    #endregion

    #region InRange

    [Fact]
    public void InRange_ShouldThrow_WhenBelowMin()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FastGuard.InRange(1, 5, 10, "param"));
    }

    [Fact]
    public void InRange_ShouldThrow_WhenAboveMax()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FastGuard.InRange(15, 5, 10, "param"));
    }

    [Fact]
    public void InRange_ShouldReturnValue_WhenInRange()
    {
        var result = FastGuard.InRange(7, 5, 10, "param");
        Assert.Equal(7, result);
    }

    [Fact]
    public void InRange_ShouldAcceptBoundaryValues()
    {
        Assert.Equal(5, FastGuard.InRange(5, 5, 10, "param"));
        Assert.Equal(10, FastGuard.InRange(10, 5, 10, "param"));
    }

    #endregion

    #region Positive

    [Fact]
    public void Positive_ShouldThrow_WhenZero()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FastGuard.Positive(0, "param"));
    }

    [Fact]
    public void Positive_ShouldThrow_WhenNegative()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FastGuard.Positive(-1, "param"));
    }

    [Fact]
    public void Positive_ShouldReturnValue_WhenPositive()
    {
        Assert.Equal(42, FastGuard.Positive(42, "param"));
    }

    #endregion

    #region NotNull<T>

    [Fact]
    public void NotNull_ShouldThrow_WhenNull()
    {
        Assert.Throws<ArgumentNullException>(() => FastGuard.NotNull<string>(null, "param"));
    }

    [Fact]
    public void NotNull_ShouldReturnValue_WhenNotNull()
    {
        var obj = new object();
        Assert.Same(obj, FastGuard.NotNull(obj, "param"));
    }

    #endregion

    #region Email

    [Fact]
    public void Email_ShouldThrow_WhenInvalid()
    {
        Assert.Throws<ArgumentException>(() => FastGuard.Email("not-an-email", "param"));
    }

    [Fact]
    public void Email_ShouldReturnValue_WhenValid()
    {
        var result = FastGuard.Email("test@example.com", "param");
        Assert.Equal("test@example.com", result);
    }

    #endregion

    #region Ascii

    [Fact]
    public void Ascii_ShouldThrow_WhenNonAscii()
    {
        Assert.Throws<ArgumentException>(() => FastGuard.Ascii("héllo".AsSpan(), "param"));
    }

    [Fact]
    public void Ascii_ShouldReturnValue_WhenAllAscii()
    {
        var result = FastGuard.Ascii("hello123".AsSpan(), "param");
        Assert.Equal("hello123", result);
    }

    #endregion

    #region AlphaNumeric

    [Fact]
    public void AlphaNumeric_ShouldThrow_WhenContainsSpecialChars()
    {
        Assert.Throws<ArgumentException>(() => FastGuard.AlphaNumeric("hello!".AsSpan(), "param"));
    }

    [Fact]
    public void AlphaNumeric_ShouldNotThrow_WhenValid()
    {
        var exception = Record.Exception(() => FastGuard.AlphaNumeric("abc123".AsSpan(), "param"));
        Assert.Null(exception);
    }

    #endregion

    #region NumericString

    [Fact]
    public void NumericString_ShouldThrow_WhenContainsLetters()
    {
        Assert.Throws<ArgumentException>(() => FastGuard.NumericString("123abc".AsSpan(), "param"));
    }

    [Fact]
    public void NumericString_ShouldNotThrow_WhenAllDigits()
    {
        var exception = Record.Exception(() => FastGuard.NumericString("123456".AsSpan(), "param"));
        Assert.Null(exception);
    }

    #endregion

    #region MaxLength

    [Fact]
    public void MaxLength_ShouldThrow_WhenExceedsMax()
    {
        Assert.Throws<ArgumentException>(() => FastGuard.MaxLength("abcdef".AsSpan(), 3, "param"));
    }

    [Fact]
    public void MaxLength_ShouldNotThrow_WhenWithinMax()
    {
        var exception = Record.Exception(() => FastGuard.MaxLength("abc".AsSpan(), 5, "param"));
        Assert.Null(exception);
    }

    #endregion

    #region ValidGuid

    [Fact]
    public void ValidGuid_ShouldThrow_WhenInvalidGuid()
    {
        Assert.Throws<ArgumentException>(() => FastGuard.ValidGuid("not-a-guid".AsSpan(), "param"));
    }

    [Fact]
    public void ValidGuid_ShouldThrow_WhenEmptyGuid()
    {
        Assert.Throws<ArgumentException>(() => FastGuard.ValidGuid(Guid.Empty.ToString().AsSpan(), "param"));
    }

    [Fact]
    public void ValidGuid_ShouldReturnGuid_WhenValid()
    {
        var expected = Guid.NewGuid();
        var result = FastGuard.ValidGuid(expected.ToString().AsSpan(), "param");
        Assert.Equal(expected, result);
    }

    #endregion

    #region Finite

    [Fact]
    public void Finite_ShouldThrow_WhenNaN()
    {
        Assert.Throws<ArgumentException>(() => FastGuard.Finite(double.NaN, "param"));
    }

    [Fact]
    public void Finite_ShouldThrow_WhenPositiveInfinity()
    {
        Assert.Throws<ArgumentException>(() => FastGuard.Finite(double.PositiveInfinity, "param"));
    }

    [Fact]
    public void Finite_ShouldThrow_WhenNegativeInfinity()
    {
        Assert.Throws<ArgumentException>(() => FastGuard.Finite(double.NegativeInfinity, "param"));
    }

    [Fact]
    public void Finite_ShouldReturnValue_WhenFinite()
    {
        Assert.Equal(3.14, FastGuard.Finite(3.14, "param"));
    }

    #endregion

    #region RegexCache

    [Fact]
    public void RegexCache_ShouldCachePatterns()
    {
        RegexCache.Clear();
        RegexCache.IsMatch("test@example.com", @"^[\w.-]+@[\w.-]+\.\w+$");
        Assert.True(RegexCache.CacheSize > 0);
    }

    [Fact]
    public void RegexCache_ShouldReturnCorrectResults()
    {
        Assert.True(RegexCache.IsMatch("12345", @"^\d+$"));
        Assert.False(RegexCache.IsMatch("abc", @"^\d+$"));
    }

    #endregion
}
