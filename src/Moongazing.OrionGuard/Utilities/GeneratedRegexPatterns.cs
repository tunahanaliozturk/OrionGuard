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

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.None, DefaultTimeoutMs)]
    public static partial Regex Email();

    [GeneratedRegex(@"^(https?|ftp)://[^\s/$.?#].[^\s]*$", RegexOptions.None, DefaultTimeoutMs)]
    public static partial Regex Url();

    [GeneratedRegex(@"^\+?[1-9]\d{1,14}$", RegexOptions.None, DefaultTimeoutMs)]
    public static partial Regex PhoneNumber();

    #endregion

    #region Character Classes

    [GeneratedRegex(@"^[a-zA-Z]+$", RegexOptions.None, DefaultTimeoutMs)]
    public static partial Regex Alphabetic();

    [GeneratedRegex(@"^\d+$", RegexOptions.None, DefaultTimeoutMs)]
    public static partial Regex Numeric();

    [GeneratedRegex(@"^[a-zA-Z0-9]+$", RegexOptions.None, DefaultTimeoutMs)]
    public static partial Regex AlphaNumeric();

    [GeneratedRegex(@"^[\x00-\x7F]+$", RegexOptions.None, DefaultTimeoutMs)]
    public static partial Regex Ascii();

    [GeneratedRegex(@"^\P{C}+$", RegexOptions.None, DefaultTimeoutMs)]
    public static partial Regex Unicode();

    [GeneratedRegex(@"^[\u1F600-\u1F64F\u1F300-\u1F5FF\u1F680-\u1F6FF\u2600-\u26FF\u2700-\u27BF]+$", RegexOptions.None, DefaultTimeoutMs)]
    public static partial Regex Emoji();

    [GeneratedRegex(@"^[A-Z0-9]+$", RegexOptions.None, DefaultTimeoutMs)]
    public static partial Regex UppercaseAlphanumeric();

    [GeneratedRegex(@"^[a-z_]+$", RegexOptions.None, DefaultTimeoutMs)]
    public static partial Regex LowercaseUnderscore();

    [GeneratedRegex(@"^[a-zA-Z0-9_]+$", RegexOptions.None, DefaultTimeoutMs)]
    public static partial Regex UsernameChars();

    #endregion

    #region Security & Authentication

    [GeneratedRegex(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&])[A-Za-z\d@$!%*?&]{8,}$", RegexOptions.None, DefaultTimeoutMs)]
    public static partial Regex StrongPassword();

    #endregion

    #region Business Formats

    [GeneratedRegex(@"^[A-Z0-9]{3,20}(-[A-Z0-9]{1,10})*$", RegexOptions.None, DefaultTimeoutMs)]
    public static partial Regex Sku();

    [GeneratedRegex(@"^[A-Z0-9]{4,20}$", RegexOptions.None, DefaultTimeoutMs)]
    public static partial Regex CouponCode();

    #endregion

    #region Financial

    [GeneratedRegex(@"^4[0-9]{12}(?:[0-9]{3})?$", RegexOptions.None, DefaultTimeoutMs)]
    public static partial Regex VisaCard();

    [GeneratedRegex(@"^5[1-5][0-9]{14}$", RegexOptions.None, DefaultTimeoutMs)]
    public static partial Regex MasterCard();

    [GeneratedRegex(@"^[A-Z]{2}[0-9]{2}[A-Z0-9]+$", RegexOptions.None, DefaultTimeoutMs)]
    public static partial Regex IbanFormat();

    #endregion

    #region International

    [GeneratedRegex(@"^(\+90|0)?5\d{9}$", RegexOptions.None, DefaultTimeoutMs)]
    public static partial Regex TurkishPhone();

    [GeneratedRegex(@"^[A-Z]{4}[A-Z]{2}[A-Z0-9]{2}([A-Z0-9]{3})?$", RegexOptions.None, DefaultTimeoutMs)]
    public static partial Regex SwiftCode();

    [GeneratedRegex(@"^[A-HJ-NPR-Z0-9]{17}$", RegexOptions.None, DefaultTimeoutMs)]
    public static partial Regex Vin();

    [GeneratedRegex(@"^[A-Z]{2}[A-Z0-9]{2,12}$", RegexOptions.None, DefaultTimeoutMs)]
    public static partial Regex VatNumber();

    #endregion

    #region Identifiers

    [GeneratedRegex(@"^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.None, DefaultTimeoutMs)]
    public static partial Regex Slug();

    [GeneratedRegex(@"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-((?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*)(?:\.(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*))*))?(?:\+([0-9a-zA-Z-]+(?:\.[0-9a-zA-Z-]+)*))?$", RegexOptions.None, DefaultTimeoutMs)]
    public static partial Regex SemVer();

    [GeneratedRegex(@"^#?([A-Fa-f0-9]{6}|[A-Fa-f0-9]{3})$", RegexOptions.None, DefaultTimeoutMs)]
    public static partial Regex HexColor();

    #endregion
}
