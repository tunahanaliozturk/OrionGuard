using System.Collections.Frozen;
using System.Text.RegularExpressions;

namespace Moongazing.OrionGuard.Extensions;

/// <summary>
/// Guards for detecting sensitive/PII data that should not appear in logs,
/// API responses, or unencrypted storage. Helps with GDPR, KVKK, PCI-DSS compliance.
/// </summary>
public static class SensitiveDataGuards
{
    // Common credit card prefixes (BIN ranges) for pattern detection
    private static readonly FrozenSet<string> CardPrefixes = new HashSet<string>
    {
        "4", // Visa
        "51", "52", "53", "54", "55", // Mastercard
        "34", "37", // Amex
        "6011", "65", // Discover
        "35", // JCB
        "30", "36", "38" // Diners
    }.ToFrozenSet();

    /// <summary>
    /// Validates that a string does not contain a credit card number pattern.
    /// Detects 13-19 digit sequences that pass basic Luhn check.
    /// Use this to prevent logging/storing raw card numbers (PCI-DSS).
    /// </summary>
    public static void AgainstContainsCreditCardNumber(this string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)) return;

        // Strip non-digits to find potential card numbers
        var digitsOnly = ExtractDigitSequences(value);
        foreach (var sequence in digitsOnly)
        {
            if (sequence.Length >= 13 && sequence.Length <= 19 && StartsWithCardPrefix(sequence) && IsValidLuhn(sequence))
            {
                throw new ArgumentException(
                    $"{parameterName} contains what appears to be a credit card number. Mask or encrypt before storing.",
                    parameterName);
            }
        }
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
    /// Validates that a string does not contain a private key or secret pattern.
    /// Detects PEM keys, AWS keys, Azure keys, JWT secrets, etc.
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

    private static bool StartsWithCardPrefix(string digits)
    {
        foreach (var prefix in CardPrefixes)
        {
            if (digits.StartsWith(prefix, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static bool IsValidLuhn(string digits)
    {
        int sum = 0;
        bool alternate = false;
        for (int i = digits.Length - 1; i >= 0; i--)
        {
            int digit = digits[i] - '0';
            if (digit < 0 || digit > 9) return false;
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

    private static List<string> ExtractDigitSequences(string input)
    {
        var results = new List<string>();
        var current = new System.Text.StringBuilder();
        foreach (var c in input)
        {
            if (char.IsDigit(c))
            {
                current.Append(c);
            }
            else
            {
                if (current.Length >= 13)
                    results.Add(current.ToString());
                current.Clear();
            }
        }
        if (current.Length >= 13)
            results.Add(current.ToString());
        return results;
    }

    #endregion
}
