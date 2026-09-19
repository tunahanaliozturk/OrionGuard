using System.Text.RegularExpressions;

namespace Moongazing.OrionGuard.Extensions;

/// <summary>
/// Guards for detecting sensitive/PII data that should not appear in logs,
/// API responses, or unencrypted storage. Helps with GDPR, KVKK, PCI-DSS compliance.
/// </summary>
/// <remarks>
/// Detection is best-effort: each guard recognizes the formats it documents and nothing else. A value that
/// passes may still carry sensitive data in another format, so treat these guards as a safety net in front
/// of logs and storage, not as proof that a value is clean.
/// </remarks>
public static partial class SensitiveDataGuards
{
    private const int MinCardDigits = 13;
    private const int MaxCardDigits = 19;

    // Common credit card BIN prefixes (kept as a small static list since StartsWith checks are O(k)).
    // Mastercard's 2221-2720 range is checked numerically in HasKnownCardPrefix.
    private static readonly string[] CardPrefixes =
    [
        "4",                               // Visa
        "51", "52", "53", "54", "55",      // Mastercard
        "34", "37",                        // Amex
        "6011", "65",                      // Discover
        "35",                              // JCB
        "30", "36", "38",                  // Diners
        "62"                               // UnionPay
    ];

    /// <summary>
    /// Bare tokens that grant access on their own: a JWT (three base64url segments, the first starting with
    /// <c>eyJ</c>, i.e. <c>{"</c>), GitHub tokens (<c>ghp_</c>, <c>gho_</c>, <c>ghs_</c>, <c>ghu_</c>,
    /// <c>ghr_</c>, <c>github_pat_</c>) and Stripe live secret and restricted keys (<c>sk_live_</c>,
    /// <c>rk_live_</c>). Each alternative starts only at a token boundary, so the scan stays linear.
    /// </summary>
    [GeneratedRegex(
        @"(?<![A-Za-z0-9_-])(?:eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]*|gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{20,}|[sr]k_live_[A-Za-z0-9]{10,})",
        RegexOptions.None, 1000)]
    private static partial Regex AccessToken();

    /// <summary>
    /// Validates that a string does not contain a credit card number pattern.
    /// Detects 13-19 digit sequences, written without separators or with single spaces or dashes between
    /// digit groups (<c>4111 1111 1111 1111</c>, <c>4111-1111-1111-1111</c>), that match a known BIN prefix
    /// (Visa, Mastercard including 2221-2720, Amex, Discover, JCB, Diners, UnionPay) and pass the Luhn check.
    /// Use this to prevent logging/storing raw card numbers (PCI-DSS). Detection is best-effort.
    /// </summary>
    /// <remarks>
    /// A candidate starts at the beginning of a digit group and ends at the end of a group, so a card number
    /// is found even when other numbers sit next to it (<c>Ref 12 4111 1111 1111 1111 12/25</c>). Each start
    /// examines at most 19 digits, so the scan is linear and allocation-free (<c>stackalloc</c> buffer).
    /// </remarks>
    public static void AgainstContainsCreditCardNumber(this string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)) return;

        if (ContainsCardNumber(value.AsSpan()))
        {
            throw new ArgumentException(
                $"{parameterName} contains what appears to be a credit card number. Mask or encrypt before storing.",
                parameterName);
        }
    }

    private static bool ContainsCardNumber(ReadOnlySpan<char> input)
    {
        Span<char> digits = stackalloc char[MaxCardDigits];

        for (int start = 0; start < input.Length; start++)
        {
            bool startsGroup = char.IsAsciiDigit(input[start]) && (start == 0 || !char.IsAsciiDigit(input[start - 1]));
            if (!startsGroup) continue;

            int count = 0;
            for (int i = start; i < input.Length && count < MaxCardDigits; i++)
            {
                char c = input[i];
                if (char.IsAsciiDigit(c))
                {
                    digits[count++] = c;
                    bool endsGroup = i + 1 == input.Length || !char.IsAsciiDigit(input[i + 1]);
                    if (endsGroup && count >= MinCardDigits &&
                        HasKnownCardPrefix(digits[..count]) && IsValidLuhn(digits[..count]))
                    {
                        return true;
                    }
                }
                else if (c is not (' ' or '-') || i + 1 == input.Length || !char.IsAsciiDigit(input[i + 1]))
                {
                    // Only a single space or dash between two digits continues a card number.
                    break;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Validates that a string does not contain an email address.
    /// Use this to prevent PII leakage in logs.
    /// </summary>
    public static void AgainstContainsEmailAddress(this string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)) return;

        if (Utilities.GeneratedRegexPatterns.EmbeddedEmail().IsMatch(value))
        {
            throw new ArgumentException(
                $"{parameterName} contains an email address. Mask PII before logging.",
                parameterName);
        }
    }

    /// <summary>
    /// Validates that a string does not contain a private key or secret pattern. Detects PEM private key
    /// headers, AWS access key ids (<c>AKIA</c>), Azure storage account keys (<c>AccountKey=</c>),
    /// <c>Bearer</c> tokens, bare JWTs, GitHub tokens (<c>ghp_</c>, <c>gho_</c>, <c>ghs_</c>, <c>ghu_</c>,
    /// <c>ghr_</c>, <c>github_pat_</c>) and Stripe live keys (<c>sk_live_</c>, <c>rk_live_</c>).
    /// Detection is best-effort: other secret formats pass.
    /// </summary>
    public static void AgainstContainsSecret(this string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)) return;

        // Check for PEM private key headers
        if (value.Contains("-----BEGIN", StringComparison.Ordinal) &&
            (value.Contains("PRIVATE KEY", StringComparison.Ordinal) || value.Contains("RSA PRIVATE", StringComparison.Ordinal)))
        {
            throw new ArgumentException(
                $"{parameterName} contains a private key.",
                parameterName);
        }

        // Check for common API key patterns (AWS Access Key)
        if (value.Contains("AKIA", StringComparison.Ordinal) && value.Length >= 20)
        {
            throw new ArgumentException(
                $"{parameterName} may contain an AWS access key.",
                parameterName);
        }

        // Check for Azure storage account key
        if (value.Contains("AccountKey=", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"{parameterName} contains an Azure storage account key.",
                parameterName);
        }

        // Check for Bearer tokens
        if (value.Contains("Bearer ", StringComparison.OrdinalIgnoreCase) && value.Contains('.'))
        {
            throw new ArgumentException(
                $"{parameterName} contains a Bearer token.",
                parameterName);
        }

        if (AccessToken().IsMatch(value))
        {
            throw new ArgumentException(
                $"{parameterName} contains what appears to be an access token or API key.",
                parameterName);
        }
    }

    /// <summary>
    /// Validates that a string does not contain a phone number pattern.
    /// </summary>
    public static void AgainstContainsPhoneNumber(this string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)) return;

        if (Utilities.GeneratedRegexPatterns.EmbeddedPhoneNumber().IsMatch(value))
        {
            throw new ArgumentException(
                $"{parameterName} contains a phone number. Mask PII before logging.",
                parameterName);
        }
    }

    /// <summary>
    /// Validates that a string does not contain an IP address.
    /// </summary>
    public static void AgainstContainsIpAddress(this string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)) return;

        var match = Utilities.GeneratedRegexPatterns.IPv4Address().Match(value);
        if (match.Success && System.Net.IPAddress.TryParse(match.Value, out _))
        {
            throw new ArgumentException(
                $"{parameterName} contains an IP address.",
                parameterName);
        }
    }

    /// <summary>
    /// Validates that a string does not contain any detectable PII.
    /// Runs all sensitive data checks: credit card, email, phone, secrets.
    /// </summary>
    public static void AgainstContainsPii(this string value, string parameterName)
    {
        value.AgainstContainsCreditCardNumber(parameterName);
        value.AgainstContainsEmailAddress(parameterName);
        value.AgainstContainsPhoneNumber(parameterName);
        value.AgainstContainsSecret(parameterName);
    }

    #region Helpers

    private static bool HasKnownCardPrefix(ReadOnlySpan<char> digits)
    {
        foreach (var prefix in CardPrefixes)
        {
            if (digits.StartsWith(prefix.AsSpan(), StringComparison.Ordinal))
                return true;
        }

        // Mastercard 2-series: 2221-2720.
        int firstFour = (digits[0] - '0') * 1000 + (digits[1] - '0') * 100 + (digits[2] - '0') * 10 + (digits[3] - '0');
        return firstFour is >= 2221 and <= 2720;
    }

    /// <summary>
    /// Luhn checksum validation on a digit span. Assumes all elements are ASCII digits;
    /// the caller guarantees this by using <see cref="char.IsAsciiDigit(char)"/> during extraction.
    /// </summary>
    private static bool IsValidLuhn(ReadOnlySpan<char> digits)
    {
        int sum = 0;
        bool alternate = false;
        for (int i = digits.Length - 1; i >= 0; i--)
        {
            int digit = digits[i] - '0';
            if (alternate)
            {
                digit *= 2;
                if (digit > 9) digit -= 9;
            }
            sum += digit;
            alternate = !alternate;
        }
        return sum % 10 == 0;
    }

    #endregion
}
