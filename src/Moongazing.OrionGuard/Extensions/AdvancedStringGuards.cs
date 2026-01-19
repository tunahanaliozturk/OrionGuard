using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;

namespace Moongazing.OrionGuard.Extensions;

/// <summary>
/// Advanced string validations for common formats like credit cards, IBANs, JSON, etc.
/// </summary>
public static class AdvancedStringGuards
{
    #region Credit Card

    /// <summary>
    /// Validates that the string is a valid credit card number using Luhn algorithm.
    /// </summary>
    public static void AgainstInvalidCreditCard(this string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || !IsValidLuhn(value.Replace(" ", "").Replace("-", "")))
        {
            throw new ArgumentException($"{parameterName} is not a valid credit card number.", parameterName);
        }
    }

    /// <summary>
    /// Validates that the string is a Visa card number.
    /// </summary>
    public static void AgainstInvalidVisaCard(this string value, string parameterName)
    {
        var cleaned = value.Replace(" ", "").Replace("-", "");
        if (!Regex.IsMatch(cleaned, @"^4[0-9]{12}(?:[0-9]{3})?$") || !IsValidLuhn(cleaned))
        {
            throw new ArgumentException($"{parameterName} is not a valid Visa card number.", parameterName);
        }
    }

    /// <summary>
    /// Validates that the string is a MasterCard number.
    /// </summary>
    public static void AgainstInvalidMasterCard(this string value, string parameterName)
    {
        var cleaned = value.Replace(" ", "").Replace("-", "");
        if (!Regex.IsMatch(cleaned, @"^5[1-5][0-9]{14}$") || !IsValidLuhn(cleaned))
        {
            throw new ArgumentException($"{parameterName} is not a valid MasterCard number.", parameterName);
        }
    }

    private static bool IsValidLuhn(string number)
    {
        if (!number.All(char.IsDigit)) return false;

        int sum = 0;
        bool alternate = false;

        for (int i = number.Length - 1; i >= 0; i--)
        {
            int digit = number[i] - '0';

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

    #region IBAN

    /// <summary>
    /// Validates that the string is a valid IBAN.
    /// </summary>
    public static void AgainstInvalidIban(this string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || !IsValidIban(value.Replace(" ", "").ToUpperInvariant()))
        {
            throw new ArgumentException($"{parameterName} is not a valid IBAN.", parameterName);
        }
    }

    private static bool IsValidIban(string iban)
    {
        if (iban.Length < 15 || iban.Length > 34) return false;
        if (!Regex.IsMatch(iban, @"^[A-Z]{2}[0-9]{2}[A-Z0-9]+$")) return false;

        // Move first 4 chars to end and convert letters to numbers
        var rearranged = iban.Substring(4) + iban.Substring(0, 4);
        var numericIban = string.Concat(rearranged.Select(c => char.IsLetter(c) ? (c - 'A' + 10).ToString() : c.ToString()));

        // Mod 97 check
        return Mod97(numericIban) == 1;
    }

    private static int Mod97(string digits)
    {
        int remainder = 0;
        foreach (char c in digits)
        {
            remainder = (remainder * 10 + (c - '0')) % 97;
        }
        return remainder;
    }

    #endregion

    #region JSON

    /// <summary>
    /// Validates that the string is valid JSON.
    /// </summary>
    public static void AgainstInvalidJson(this string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{parameterName} is not valid JSON.", parameterName);
        }

        try
        {
            JsonDocument.Parse(value);
        }
        catch (JsonException)
        {
            throw new ArgumentException($"{parameterName} is not valid JSON.", parameterName);
        }
    }

    /// <summary>
    /// Validates that the string is a valid JSON object (not array or primitive).
    /// </summary>
    public static void AgainstInvalidJsonObject(this string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{parameterName} is not a valid JSON object.", parameterName);
        }

        try
        {
            using var doc = JsonDocument.Parse(value);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException($"{parameterName} is not a valid JSON object.", parameterName);
            }
        }
        catch (JsonException)
        {
            throw new ArgumentException($"{parameterName} is not a valid JSON object.", parameterName);
        }
    }

    #endregion

    #region XML

    /// <summary>
    /// Validates that the string is valid XML.
    /// </summary>
    public static void AgainstInvalidXml(this string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{parameterName} is not valid XML.", parameterName);
        }

        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(value);
        }
        catch (XmlException)
        {
            throw new ArgumentException($"{parameterName} is not valid XML.", parameterName);
        }
    }

    #endregion

    #region Base64

    /// <summary>
    /// Validates that the string is valid Base64.
    /// </summary>
    public static void AgainstInvalidBase64(this string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{parameterName} is not valid Base64.", parameterName);
        }

        try
        {
            Convert.FromBase64String(value);
        }
        catch (FormatException)
        {
            throw new ArgumentException($"{parameterName} is not valid Base64.", parameterName);
        }
    }

    #endregion

    #region Phone Number

    /// <summary>
    /// Validates that the string is a valid phone number in E.164 format.
    /// </summary>
    public static void AgainstInvalidPhoneNumber(this string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || !Regex.IsMatch(value, @"^\+?[1-9]\d{1,14}$"))
        {
            throw new ArgumentException($"{parameterName} is not a valid phone number.", parameterName);
        }
    }

    /// <summary>
    /// Validates that the string is a valid Turkish phone number.
    /// </summary>
    public static void AgainstInvalidTurkishPhoneNumber(this string value, string parameterName)
    {
        var cleaned = value.Replace(" ", "").Replace("-", "").Replace("(", "").Replace(")", "");
        if (!Regex.IsMatch(cleaned, @"^(\+90|0)?5\d{9}$"))
        {
            throw new ArgumentException($"{parameterName} is not a valid Turkish phone number.", parameterName);
        }
    }

    #endregion

    #region Turkish ID (TC Kimlik No)

    /// <summary>
    /// Validates that the string is a valid Turkish ID number (TC Kimlik No).
    /// </summary>
    public static void AgainstInvalidTurkishId(this string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || !IsValidTurkishId(value))
        {
            throw new ArgumentException($"{parameterName} is not a valid Turkish ID number.", parameterName);
        }
    }

    private static bool IsValidTurkishId(string tcNo)
    {
        if (tcNo.Length != 11 || !tcNo.All(char.IsDigit) || tcNo[0] == '0')
            return false;

        int[] digits = tcNo.Select(c => c - '0').ToArray();

        int oddSum = digits[0] + digits[2] + digits[4] + digits[6] + digits[8];
        int evenSum = digits[1] + digits[3] + digits[5] + digits[7];

        int digit10 = ((oddSum * 7) - evenSum) % 10;
        if (digit10 < 0) digit10 += 10;

        int digit11 = (digits.Take(10).Sum()) % 10;

        return digits[9] == digit10 && digits[10] == digit11;
    }

    #endregion

    #region Slug

    /// <summary>
    /// Validates that the string is a valid URL slug.
    /// </summary>
    public static void AgainstInvalidSlug(this string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || !Regex.IsMatch(value, @"^[a-z0-9]+(?:-[a-z0-9]+)*$"))
        {
            throw new ArgumentException($"{parameterName} is not a valid URL slug.", parameterName);
        }
    }

    #endregion

    #region Semantic Version

    /// <summary>
    /// Validates that the string is a valid semantic version (SemVer).
    /// </summary>
    public static void AgainstInvalidSemVer(this string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !Regex.IsMatch(value, @"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-((?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*)(?:\.(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*))*))?(?:\+([0-9a-zA-Z-]+(?:\.[0-9a-zA-Z-]+)*))?$"))
        {
            throw new ArgumentException($"{parameterName} is not a valid semantic version.", parameterName);
        }
    }

    #endregion

    #region Hex Color

    /// <summary>
    /// Validates that the string is a valid hex color code.
    /// </summary>
    public static void AgainstInvalidHexColor(this string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || !Regex.IsMatch(value, @"^#?([A-Fa-f0-9]{6}|[A-Fa-f0-9]{3})$"))
        {
            throw new ArgumentException($"{parameterName} is not a valid hex color code.", parameterName);
        }
    }

    #endregion
}
