namespace Moongazing.OrionGuard.Extensions;

/// <summary>
/// International format validation guards for SWIFT codes, ISBNs, VINs, EANs, VAT numbers, and IMEIs.
/// </summary>
public static class InternationalGuards
{
    #region SWIFT/BIC

    /// <summary>
    /// Validates that the string is a valid SWIFT/BIC code.
    /// Format: 4 letters (bank) + 2 letters (country) + 2 alphanumeric (location) + optional 3 alphanumeric (branch).
    /// </summary>
    public static void AgainstInvalidSwiftCode(this string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !Utilities.GeneratedRegexPatterns.SwiftCode().IsMatch(value.ToUpperInvariant()))
        {
            throw new ArgumentException($"{parameterName} is not a valid SWIFT/BIC code.", parameterName);
        }
    }

    #endregion

    #region ISBN

    /// <summary>
    /// Validates that the string is a valid ISBN-10 or ISBN-13.
    /// Hyphens and spaces are stripped before validation.
    /// </summary>
    public static void AgainstInvalidIsbn(this string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{parameterName} is not a valid ISBN.", parameterName);
        }

        var cleaned = value.Replace("-", "").Replace(" ", "");

        if (!IsValidIsbn10(cleaned) && !IsValidIsbn13(cleaned))
        {
            throw new ArgumentException($"{parameterName} is not a valid ISBN.", parameterName);
        }
    }

    private static bool IsValidIsbn10(string isbn)
    {
        if (isbn.Length != 10) return false;

        int sum = 0;
        for (int i = 0; i < 9; i++)
        {
            if (!char.IsDigit(isbn[i])) return false;
            sum += (isbn[i] - '0') * (10 - i);
        }

        // Last digit can be 'X' representing 10
        char last = isbn[9];
        if (last == 'X' || last == 'x')
        {
            sum += 10;
        }
        else if (char.IsDigit(last))
        {
            sum += last - '0';
        }
        else
        {
            return false;
        }

        return sum % 11 == 0;
    }

    private static bool IsValidIsbn13(string isbn)
    {
        if (isbn.Length != 13 || !isbn.All(char.IsDigit)) return false;
        if (!isbn.StartsWith("978", StringComparison.Ordinal) && !isbn.StartsWith("979", StringComparison.Ordinal)) return false;

        int sum = 0;
        for (int i = 0; i < 13; i++)
        {
            int digit = isbn[i] - '0';
            sum += (i % 2 == 0) ? digit : digit * 3;
        }

        return sum % 10 == 0;
    }

    #endregion

    #region VIN

    /// <summary>
    /// Validates that the string is a valid Vehicle Identification Number (VIN).
    /// Must be exactly 17 alphanumeric characters, excluding I, O, and Q.
    /// </summary>
    public static void AgainstInvalidVin(this string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !Utilities.GeneratedRegexPatterns.Vin().IsMatch(value.ToUpperInvariant()))
        {
            throw new ArgumentException($"{parameterName} is not a valid VIN.", parameterName);
        }
    }

    #endregion

    #region EAN

    /// <summary>
    /// Validates that the string is a valid EAN-13 barcode.
    /// Must be exactly 13 digits with a valid modulo 10 check digit.
    /// </summary>
    public static void AgainstInvalidEan(this string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length != 13 || !value.All(char.IsDigit))
        {
            throw new ArgumentException($"{parameterName} is not a valid EAN-13 barcode.", parameterName);
        }

        int sum = 0;
        for (int i = 0; i < 13; i++)
        {
            int digit = value[i] - '0';
            sum += (i % 2 == 0) ? digit : digit * 3;
        }

        if (sum % 10 != 0)
        {
            throw new ArgumentException($"{parameterName} is not a valid EAN-13 barcode.", parameterName);
        }
    }

    #endregion

    #region VAT Number

    /// <summary>
    /// Validates that the string is a valid EU VAT number format.
    /// Must start with a 2-letter country code followed by 2-12 alphanumeric characters.
    /// This is a format check only, not a tax authority verification.
    /// </summary>
    public static void AgainstInvalidVatNumber(this string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !Utilities.GeneratedRegexPatterns.VatNumber().IsMatch(value.ToUpperInvariant()))
        {
            throw new ArgumentException($"{parameterName} is not a valid EU VAT number format.", parameterName);
        }
    }

    #endregion

    #region IMEI

    /// <summary>
    /// Validates that the string is a valid IMEI (International Mobile Equipment Identity).
    /// Must be exactly 15 digits and pass the Luhn check.
    /// </summary>
    public static void AgainstInvalidImei(this string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length != 15 || !value.All(char.IsDigit))
        {
            throw new ArgumentException($"{parameterName} is not a valid IMEI.", parameterName);
        }

        if (!IsValidLuhn(value))
        {
            throw new ArgumentException($"{parameterName} is not a valid IMEI.", parameterName);
        }
    }

    private static bool IsValidLuhn(string number)
    {
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
}
