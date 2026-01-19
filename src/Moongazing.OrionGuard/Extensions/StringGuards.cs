using Moongazing.OrionGuard.Exceptions;
using System.Text.RegularExpressions;

namespace Moongazing.OrionGuard.Extensions;

/// <summary>
/// Provides validation methods for strings.
/// </summary>
public static class StringGuards
{
    /// <summary>
    /// Validates that a string is not null or empty.
    /// </summary>
    public static void AgainstNullOrEmpty(this string value, string parameterName)
    {
        if (string.IsNullOrEmpty(value))
        {
            throw new ArgumentException($"{parameterName} cannot be null or empty.", parameterName);
        }
    }

    /// <summary>
    /// Validates that a string is not null, empty, or whitespace.
    /// </summary>
    public static void AgainstNullOrWhiteSpace(this string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{parameterName} cannot be null, empty, or whitespace.", parameterName);
        }
    }

    /// <summary>
    /// Validates that a string does not contain only whitespace characters.
    /// </summary>
    public static void AgainstOnlyWhiteSpace(this string value, string parameterName)
    {
        if (!string.IsNullOrEmpty(value) && value.Trim().Length == 0)
        {
            throw new ArgumentException($"{parameterName} cannot consist only of whitespace.", parameterName);
        }
    }

    /// <summary>
    /// Validates that a string's length is within a specified range.
    /// </summary>
    public static void AgainstInvalidLength(this string value, int minLength, int maxLength, string parameterName)
    {
        if (value.Length < minLength || value.Length > maxLength)
        {
            throw new ArgumentException($"{parameterName} must be between {minLength} and {maxLength} characters long.", parameterName);
        }
    }

    /// <summary>
    /// Validates that a string contains only letters.
    /// </summary>
    public static void AgainstNonAlphabeticCharacters(this string value, string parameterName)
    {
        if (!Regex.IsMatch(value, @"^[a-zA-Z]+$"))
        {
            throw new ArgumentException($"{parameterName} must contain only alphabetic characters.", parameterName);
        }
    }

    /// <summary>
    /// Validates that a string contains only digits.
    /// </summary>
    public static void AgainstNonNumericCharacters(this string value, string parameterName)
    {
        if (!Regex.IsMatch(value, @"^\d+$"))
        {
            throw new ArgumentException($"{parameterName} must contain only numeric characters.", parameterName);
        }
    }

    /// <summary>
    /// Validates that a string contains only alphanumeric characters.
    /// </summary>
    public static void AgainstNonAlphanumericCharacters(this string value, string parameterName)
    {
        if (!Regex.IsMatch(value, @"^[a-zA-Z0-9]+$"))
        {
            throw new ArgumentException($"{parameterName} must contain only alphanumeric characters.", parameterName);
        }
    }

    /// <summary>
    /// Validates that a string starts with a specific substring.
    /// </summary>
    public static void AgainstNotStartingWith(this string value, string prefix, string parameterName)
    {
        if (!value.StartsWith(prefix))
        {
            throw new ArgumentException($"{parameterName} must start with '{prefix}'.", parameterName);
        }
    }

    /// <summary>
    /// Validates that a string ends with a specific substring.
    /// </summary>
    public static void AgainstNotEndingWith(this string value, string suffix, string parameterName)
    {
        if (!value.EndsWith(suffix))
        {
            throw new ArgumentException($"{parameterName} must end with '{suffix}'.", parameterName);
        }
    }

    /// <summary>
    /// Validates that a string contains only ASCII characters.
    /// </summary>
    public static void AgainstNonAsciiCharacters(this string value, string parameterName)
    {
        if (!Regex.IsMatch(value, @"^[\x00-\x7F]+$"))
        {
            throw new ArgumentException($"{parameterName} must contain only ASCII characters.", parameterName);
        }
    }

    /// <summary>
    /// Validates that a string contains only Unicode characters.
    /// </summary>
    public static void AgainstNonUnicodeCharacters(this string value, string parameterName)
    {
        if (!Regex.IsMatch(value, @"^\P{C}+$"))
        {
            throw new ArgumentException($"{parameterName} must contain only Unicode characters.", parameterName);
        }
    }

    /// <summary>
    /// Validates that a string matches a specific regex pattern.
    /// </summary>
    public static void AgainstRegexMismatch(this string value, string pattern, string parameterName)
    {
        if (!Regex.IsMatch(value, pattern))
        {
            throw new ArgumentException($"{parameterName} does not match the required pattern.", parameterName);
        }
    }

    /// <summary>
    /// Validates that a string does not contain a specific substring.
    /// </summary>
    public static void AgainstContainingSubstring(this string value, string forbiddenSubstring, string parameterName)
    {
        if (value.Contains(forbiddenSubstring))
        {
            throw new ArgumentException($"{parameterName} must not contain the forbidden substring '{forbiddenSubstring}'.", parameterName);
        }
    }

    /// <summary>
    /// Validates that a string is all uppercase.
    /// </summary>
    public static void AgainstNotAllUppercase(this string value, string parameterName)
    {
        if (value != value.ToUpperInvariant())
        {
            throw new ArgumentException($"{parameterName} must be all uppercase.", parameterName);
        }
    }

    /// <summary>
    /// Validates that a string is all lowercase.
    /// </summary>
    public static void AgainstNotAllLowercase(this string value, string parameterName)
    {
        if (!value.Equals(value, StringComparison.InvariantCultureIgnoreCase))
        {
            throw new ArgumentException($"{parameterName} must be all lowercase.", parameterName);
        }
    }

    /// <summary>
    /// Validates that the provided string is a valid email address.
    /// </summary>
    /// <param name="email">The email string to validate.</param>
    /// <param name="parameterName">The parameter name for error messages.</param>
    public static void AgainstInvalidEmail(this string email, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(email) ||
            !Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
        {
            throw new InvalidEmailException(parameterName);
        }
    }
    /// <summary>
    /// Validates that the provided string is a required length.
    /// </summary>
    /// <param name="value"></param>
    /// <param name="requiredLength"></param>
    /// <param name="parameterName"></param>
    /// <exception cref="ArgumentException"></exception>
    public static void AgainstLengthMismatch(this string value, int requiredLength, string parameterName)
    {
        if (value.Length != requiredLength)
        {
            throw new ArgumentException($"{parameterName} must be exactly {requiredLength} characters long.", parameterName);
        }
    }
    /// <summary>
    /// Validates that the provided string is contain white spaces.
    /// </summary>
    /// <param name="value"></param>
    /// <param name="parameterName"></param>
    /// <exception cref="ArgumentException"></exception>
    public static void AgainstContainingWhitespace(this string value, string parameterName)
    {
        if (value.Contains(' '))
        {
            throw new ArgumentException($"{parameterName} must not contain whitespace.", parameterName);
        }
    }
    public static void AgainstCharactersOutsideSet(this string value, string allowedCharacters, string parameterName)
    {
        if (!Regex.IsMatch(value, $"^[{Regex.Escape(allowedCharacters)}]+$"))
        {
            throw new ArgumentException($"{parameterName} must only contain characters from the set '{allowedCharacters}'.", parameterName);
        }
    }

    public static void AgainstContainingAnySubstrings(this string value, string[] forbiddenSubstrings, string parameterName)
    {
        foreach (var forbiddenSubstring in forbiddenSubstrings)
        {
            if (value.Contains(forbiddenSubstring))
            {
                throw new ArgumentException($"{parameterName} must not contain the forbidden substring '{forbiddenSubstring}'.", parameterName);
            }
        }
    }
    public static void AgainstNonPalindrome(this string value, string parameterName)
    {
        var charArray = value.ToCharArray();
        Array.Reverse(charArray);
        var reversed = new string(charArray);
        if (!value.Equals(reversed, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"{parameterName} must be a palindrome.", parameterName);
        }
    }
    public static void AgainstInvalidPhoneNumber(this string value, string parameterName)
    {
        if (!Regex.IsMatch(value, @"^\+?[1-9]\d{1,14}$"))
        {
            throw new ArgumentException($"{parameterName} must be a valid phone number.", parameterName);
        }
    }
    public static void AgainstExceedingCharacterCount(this string value, char character, int maxCount, string parameterName)
    {
        int count = value.Count(c => c == character);
        if (count > maxCount)
        {
            throw new ArgumentException($"{parameterName} must not contain more than {maxCount} occurrences of '{character}'.", parameterName);
        }
    }
    public static void AgainstInvalidUrl(this string value, string parameterName)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uriResult) ||
            !(uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps))
        {
            throw new ArgumentException($"{parameterName} must be a valid URL.", parameterName);
        }
    }
    public static void AgainstNonEmojiCharacters(this string value, string parameterName)
    {
        if (!Regex.IsMatch(value, @"^[\u1F600-\u1F64F\u1F300-\u1F5FF\u1F680-\u1F6FF\u2600-\u26FF\u2700-\u27BF]+$"))
        {
            throw new ArgumentException($"{parameterName} must only contain emoji characters.", parameterName);
        }
    }

    public static void AgainstCharacterCountMismatch(this string value, char character, int exactCount, string parameterName)
    {
        int count = value.Count(c => c == character);
        if (count != exactCount)
        {
            throw new ArgumentException($"{parameterName} must contain exactly {exactCount} occurrences of '{character}'.", parameterName);
        }
    }
    public static void AgainstNonUppercaseAlphanumeric(this string value, string parameterName)
    {
        if (!Regex.IsMatch(value, @"^[A-Z0-9]+$"))
        {
            throw new ArgumentException($"{parameterName} must contain only uppercase letters and numbers.", parameterName);
        }
    }


    public static void AgainstNonLowercaseUnderscore(this string value, string parameterName)
    {
        if (!Regex.IsMatch(value, @"^[a-z_]+$"))
        {
            throw new ArgumentException($"{parameterName} must contain only lowercase letters and underscores.", parameterName);
        }
    }

    public static void AgainstNotStartingWithAny(this string value, string[] prefixes, string parameterName)
    {
        if (!prefixes.Any(prefix => value.StartsWith(prefix)))
        {
            throw new ArgumentException($"{parameterName} must start with one of the following prefixes: {string.Join(", ", prefixes)}.", parameterName);
        }
    }
    public static void AgainstNotEndingWithAny(this string value, string[] suffixes, string parameterName)
    {
        if (!suffixes.Any(suffix => value.EndsWith(suffix)))
        {
            throw new ArgumentException($"{parameterName} must end with one of the following suffixes: {string.Join(", ", suffixes)}.", parameterName);
        }
    }
    public static void AgainstNotMatchingPrefixAndSuffix(this string value, string prefix, string suffix, string parameterName)
    {
        if (!value.StartsWith(prefix) || !value.EndsWith(suffix))
        {
            throw new ArgumentException($"{parameterName} must start with '{prefix}' and end with '{suffix}'.", parameterName);
        }
    }
    public static void AgainstInvalidIpAddress(this string value, string parameterName)
    {
        if (!System.Net.IPAddress.TryParse(value, out _))
        {
            throw new ArgumentException($"{parameterName} must be a valid IP address.", parameterName);
        }
    }
    public static void AgainstInvalidGuid(this string value, string parameterName)
    {
        if (!Guid.TryParse(value, out _))
        {
            throw new ArgumentException($"{parameterName} must be a valid GUID.", parameterName);
        }
    }
    public static void AgainstWeakPassword(this string value, string parameterName)
    {
        if (!Regex.IsMatch(value, @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&])[A-Za-z\d@$!%*?&]{8,}$"))
        {
            throw new ArgumentException($"{parameterName} must be a strong password (minimum 8 characters, including uppercase, lowercase, number, and special character).", parameterName);
        }
    }


}
