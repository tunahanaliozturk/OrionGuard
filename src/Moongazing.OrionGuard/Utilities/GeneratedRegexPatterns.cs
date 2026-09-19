using System.Text.RegularExpressions;

namespace Moongazing.OrionGuard.Utilities;

/// <summary>
/// Source-generated regex patterns for zero-allocation, NativeAOT-compatible validation.
/// Replaces runtime-compiled RegexCache patterns with compile-time generated code.
/// </summary>
public static partial class GeneratedRegexPatterns
{
    private const int DefaultTimeoutMs = 1000;

    #region Common Formats

    /// <summary>
    /// The email pattern, shared with the pattern-string APIs (<c>GuardProfiles.Email</c>,
    /// <c>RegexPatterns.Email</c>) so every email check accepts the same set.
    /// </summary>
    /// <remarks>
    /// The leading lookahead caps the address at 254 characters (the RFC 5321 path limit) before anything
    /// else runs, and the domain labels exclude '.', so the pattern does linear, bounded work on hostile
    /// input. The previous pattern let <c>[^@\s]+\.</c> backtrack over every dot in the domain, which made
    /// long inputs quadratic.
    /// </remarks>
    internal const string EmailPattern = @"^(?=.{1,254}\z)[^@\s]+@[^@\s.]+(?:\.[^@\s.]+)+\z";

    [GeneratedRegex(EmailPattern, RegexOptions.None, DefaultTimeoutMs)]
    public static partial Regex Email();

    [GeneratedRegex(@"^(https?|ftp)://[^\s/$.?#].[^\s]*\z", RegexOptions.None, DefaultTimeoutMs)]
    public static partial Regex Url();

    [GeneratedRegex(@"^\+?[1-9][0-9]{1,14}\z", RegexOptions.None, DefaultTimeoutMs)]
    public static partial Regex PhoneNumber();

    #endregion

    #region Character Classes

    [GeneratedRegex(@"^[a-zA-Z]+\z", RegexOptions.None, DefaultTimeoutMs)]
    public static partial Regex Alphabetic();

    [GeneratedRegex(@"^[0-9]+\z", RegexOptions.None, DefaultTimeoutMs)]
    public static partial Regex Numeric();

    [GeneratedRegex(@"^[a-zA-Z0-9]+\z", RegexOptions.None, DefaultTimeoutMs)]
    public static partial Regex AlphaNumeric();

    [GeneratedRegex(@"^[\x00-\x7F]+\z", RegexOptions.None, DefaultTimeoutMs)]
    public static partial Regex Ascii();

    [GeneratedRegex(@"^\P{C}+\z", RegexOptions.None, DefaultTimeoutMs)]
    public static partial Regex Unicode();

    /// <summary>
    /// Matches a run of emoji characters <b>anywhere</b> in the input: this pattern is unanchored, so
    /// <c>IsMatch</c> answers "contains an emoji", not "is only emoji". <see cref="EmojiOnly"/> is the
    /// anchored form.
    /// </summary>
    [GeneratedRegex(@"[\p{So}\p{Cs}]+", RegexOptions.None, DefaultTimeoutMs)]
    public static partial Regex Emoji();

    /// <summary>
    /// Anchored form of <see cref="Emoji"/>: the whole value must be emoji, and every sequence in it must be
    /// complete. One unit is a keycap (<c>1️⃣</c>: a digit, <c>#</c> or <c>*</c> plus the combining enclosing
    /// keycap) or an emoji character - an Other Symbol such as ❤, or a well-formed surrogate pair, which is
    /// how every emoji above U+FFFF is stored - with an optional variation selector. A zero-width joiner
    /// counts only when another unit follows it, so a value ending in one is rejected, and a lone surrogate
    /// is not a unit at all. Each alternative starts with a different character, so matching stays linear.
    /// </summary>
    [GeneratedRegex(
        "^(?:" + EmojiUnit + "(?:\\u200D" + EmojiUnit + ")*)+\\z",
        RegexOptions.None,
        DefaultTimeoutMs)]
    internal static partial Regex EmojiOnly();

    // A surrogate is only an emoji as a complete high/low pair; \p{Cs} alone also matches half of one.
    private const string EmojiUnit =
        "(?:[0-9#*]\\uFE0F?\\u20E3|(?:[\\uD800-\\uDBFF][\\uDC00-\\uDFFF]|\\p{So})[\\uFE0E\\uFE0F]?)";

    [GeneratedRegex(@"^[A-Z0-9]+\z", RegexOptions.None, DefaultTimeoutMs)]
    public static partial Regex UppercaseAlphanumeric();

    [GeneratedRegex(@"^[a-z_]+\z", RegexOptions.None, DefaultTimeoutMs)]
    public static partial Regex LowercaseUnderscore();

    [GeneratedRegex(@"^[a-zA-Z0-9_]+\z", RegexOptions.None, DefaultTimeoutMs)]
    public static partial Regex UsernameChars();

    #endregion

    #region Security & Authentication

    [GeneratedRegex(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*[0-9])(?=.*[@$!%*?&])[A-Za-z0-9@$!%*?&]{8,}\z", RegexOptions.None, DefaultTimeoutMs)]
    public static partial Regex StrongPassword();

    #endregion

    #region Business Formats

    [GeneratedRegex(@"^[A-Z0-9]{3,20}(-[A-Z0-9]{1,10})*\z", RegexOptions.None, DefaultTimeoutMs)]
    public static partial Regex Sku();

    [GeneratedRegex(@"^[A-Z0-9]{4,20}\z", RegexOptions.None, DefaultTimeoutMs)]
    public static partial Regex CouponCode();

    #endregion

    #region Financial

    [GeneratedRegex(@"^4[0-9]{12}(?:[0-9]{3})?\z", RegexOptions.None, DefaultTimeoutMs)]
    public static partial Regex VisaCard();

    /// <summary>
    /// Mastercard PANs: the original <c>51</c>-<c>55</c> range and the <c>2221</c>-<c>2720</c> range
    /// Mastercard added in 2017, both 16 digits.
    /// </summary>
    [GeneratedRegex(@"^(?:5[1-5][0-9]{2}|222[1-9]|22[3-9][0-9]|2[3-6][0-9]{2}|27[01][0-9]|2720)[0-9]{12}\z", RegexOptions.None, DefaultTimeoutMs)]
    public static partial Regex MasterCard();

    [GeneratedRegex(@"^[A-Z]{2}[0-9]{2}[A-Z0-9]+\z", RegexOptions.None, DefaultTimeoutMs)]
    public static partial Regex IbanFormat();

    #endregion

    #region International

    [GeneratedRegex(@"^(\+90|0)?5[0-9]{9}\z", RegexOptions.None, DefaultTimeoutMs)]
    public static partial Regex TurkishPhone();

    [GeneratedRegex(@"^[A-Z]{4}[A-Z]{2}[A-Z0-9]{2}([A-Z0-9]{3})?\z", RegexOptions.None, DefaultTimeoutMs)]
    public static partial Regex SwiftCode();

    [GeneratedRegex(@"^[A-HJ-NPR-Z0-9]{17}\z", RegexOptions.None, DefaultTimeoutMs)]
    public static partial Regex Vin();

    [GeneratedRegex(@"^[A-Z]{2}[A-Z0-9]{2,12}\z", RegexOptions.None, DefaultTimeoutMs)]
    public static partial Regex VatNumber();

    #endregion

    #region Sensitive Data Detection

    [GeneratedRegex(@"\b[0-9]{13,19}\b", RegexOptions.None, DefaultTimeoutMs)]
    public static partial Regex CreditCardDigitSequence();

    [GeneratedRegex(@"\b(?:[0-9]{1,3}\.){3}[0-9]{1,3}\b", RegexOptions.None, DefaultTimeoutMs)]
    public static partial Regex IPv4Address();

    [GeneratedRegex(@"[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}", RegexOptions.None, DefaultTimeoutMs)]
    public static partial Regex EmbeddedEmail();

    [GeneratedRegex(@"(?:\+?\d{1,3}[\s\-]?)?\(?\d{2,4}\)?[\s\-]?\d{3,4}[\s\-]?\d{2,4}", RegexOptions.None, DefaultTimeoutMs)]
    public static partial Regex EmbeddedPhoneNumber();

    #endregion

    #region Identifiers

    [GeneratedRegex(@"^[a-z0-9]+(?:-[a-z0-9]+)*\z", RegexOptions.None, DefaultTimeoutMs)]
    public static partial Regex Slug();

    [GeneratedRegex(@"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-((?:0|[1-9][0-9]*|[0-9]*[a-zA-Z-][0-9a-zA-Z-]*)(?:\.(?:0|[1-9][0-9]*|[0-9]*[a-zA-Z-][0-9a-zA-Z-]*))*))?(?:\+([0-9a-zA-Z-]+(?:\.[0-9a-zA-Z-]+)*))?\z", RegexOptions.None, DefaultTimeoutMs)]
    public static partial Regex SemVer();

    [GeneratedRegex(@"^#?([A-Fa-f0-9]{6}|[A-Fa-f0-9]{3})\z", RegexOptions.None, DefaultTimeoutMs)]
    public static partial Regex HexColor();

    #endregion
}
