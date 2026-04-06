using System.Collections.Frozen;
using Moongazing.OrionGuard.Core;

namespace Moongazing.OrionGuard.Extensions;

/// <summary>
/// Business domain specific validation guards.
/// </summary>
public static class BusinessGuards
{
    private static readonly FrozenSet<string> ValidCurrencyCodes = new HashSet<string>
    {
        "USD", "EUR", "GBP", "JPY", "CHF", "AUD", "CAD", "CNY", "INR", "MXN",
        "BRL", "RUB", "KRW", "SGD", "HKD", "NOK", "SEK", "DKK", "NZD", "ZAR",
        "TRY", "PLN", "THB", "IDR", "MYR", "PHP", "CZK", "ILS", "CLP", "PKR",
        "EGP", "AED", "SAR", "QAR", "KWD", "BHD", "OMR", "JOD", "LBP", "MAD"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    #region Money & Currency

    /// <summary>
    /// Validates that the value is a valid monetary amount (non-negative, max 2 decimal places).
    /// </summary>
    public static void AgainstInvalidMonetaryAmount(this decimal value, string parameterName, int maxDecimalPlaces = 2)
    {
        if (value < 0)
        {
            throw new ArgumentException($"{parameterName} cannot be negative.", parameterName);
        }

        var decimalPlaces = BitConverter.GetBytes(decimal.GetBits(value)[3])[2];
        if (decimalPlaces > maxDecimalPlaces)
        {
            throw new ArgumentException($"{parameterName} cannot have more than {maxDecimalPlaces} decimal places.", parameterName);
        }
    }

    /// <summary>
    /// Validates that the currency code is a valid ISO 4217 code.
    /// </summary>
    public static void AgainstInvalidCurrencyCode(this string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || !ValidCurrencyCodes.Contains(value))
        {
            throw new ArgumentException($"{parameterName} is not a valid ISO 4217 currency code.", parameterName);
        }
    }

    /// <summary>
    /// Validates that the percentage is between 0 and 100.
    /// </summary>
    public static void AgainstInvalidPercentage(this decimal value, string parameterName, bool allowOver100 = false)
    {
        if (value < 0 || (!allowOver100 && value > 100))
        {
            throw new ArgumentOutOfRangeException(parameterName, $"{parameterName} must be between 0 and 100.");
        }
    }

    #endregion

    #region Quantity & Inventory

    /// <summary>
    /// Validates that the quantity is valid (positive integer).
    /// </summary>
    public static void AgainstInvalidQuantity(this int value, string parameterName, int minQuantity = 1)
    {
        if (value < minQuantity)
        {
            throw new ArgumentOutOfRangeException(parameterName, $"{parameterName} must be at least {minQuantity}.");
        }
    }

    /// <summary>
    /// Validates that the SKU is valid format.
    /// </summary>
    public static void AgainstInvalidSku(this string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !Utilities.GeneratedRegexPatterns.Sku().IsMatch(value))
        {
            throw new ArgumentException($"{parameterName} is not a valid SKU format.", parameterName);
        }
    }

    #endregion

    #region Date & Time Business Rules

    /// <summary>
    /// Validates that the date is a valid business day (not weekend).
    /// </summary>
    public static void AgainstWeekend(this DateTime value, string parameterName)
    {
        if (value.DayOfWeek == DayOfWeek.Saturday || value.DayOfWeek == DayOfWeek.Sunday)
        {
            throw new ArgumentException($"{parameterName} cannot be on a weekend.", parameterName);
        }
    }

    /// <summary>
    /// Validates that the date is within business hours.
    /// </summary>
    public static void AgainstOutsideBusinessHours(this DateTime value, string parameterName, int startHour = 9, int endHour = 17)
    {
        if (value.Hour < startHour || value.Hour >= endHour)
        {
            throw new ArgumentException($"{parameterName} must be within business hours ({startHour}:00 - {endHour}:00).", parameterName);
        }
    }

    /// <summary>
    /// Validates that the date range is valid (start before end).
    /// </summary>
    public static void AgainstInvalidDateRange(this (DateTime Start, DateTime End) range, string parameterName)
    {
        if (range.Start >= range.End)
        {
            throw new ArgumentException($"{parameterName} start date must be before end date.", parameterName);
        }
    }

    /// <summary>
    /// Validates that the subscription period is valid (1-12 months or 1-5 years).
    /// </summary>
    public static void AgainstInvalidSubscriptionPeriod(this int months, string parameterName)
    {
        var validPeriods = new[] { 1, 3, 6, 12, 24, 36, 48, 60 };
        if (!validPeriods.Contains(months))
        {
            throw new ArgumentException($"{parameterName} must be a valid subscription period (1, 3, 6, 12, 24, 36, 48, or 60 months).", parameterName);
        }
    }

    #endregion

    #region User & Account

    /// <summary>
    /// Validates that the age is within valid range for account creation.
    /// </summary>
    public static void AgainstInvalidAccountAge(this int age, string parameterName, int minAge = 13, int maxAge = 120)
    {
        if (age < minAge || age > maxAge)
        {
            throw new ArgumentOutOfRangeException(parameterName, $"{parameterName} must be between {minAge} and {maxAge}.");
        }
    }

    /// <summary>
    /// Validates that the role is valid.
    /// </summary>
    public static void AgainstInvalidRole(this string value, string parameterName, params string[] validRoles)
    {
        if (!validRoles.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"{parameterName} is not a valid role. Valid roles: {string.Join(", ", validRoles)}.", parameterName);
        }
    }

    /// <summary>
    /// Validates that the status transition is valid.
    /// </summary>
    public static void AgainstInvalidStatusTransition(this string currentStatus, string newStatus, Dictionary<string, string[]> validTransitions, string parameterName)
    {
        if (!validTransitions.TryGetValue(currentStatus, out var allowed) || !allowed.Contains(newStatus))
        {
            throw new InvalidOperationException($"Cannot transition {parameterName} from '{currentStatus}' to '{newStatus}'.");
        }
    }

    #endregion

    #region E-commerce

    /// <summary>
    /// Validates that the order total meets minimum requirements.
    /// </summary>
    public static void AgainstOrderBelowMinimum(this decimal total, decimal minimumOrder, string parameterName)
    {
        if (total < minimumOrder)
        {
            throw new ArgumentException($"{parameterName} must be at least {minimumOrder:C}.", parameterName);
        }
    }

    /// <summary>
    /// Validates that the discount percentage is valid.
    /// </summary>
    public static void AgainstInvalidDiscount(this decimal discount, string parameterName, decimal maxDiscount = 100m)
    {
        if (discount < 0 || discount > maxDiscount)
        {
            throw new ArgumentOutOfRangeException(parameterName, $"{parameterName} must be between 0 and {maxDiscount}%.");
        }
    }

    /// <summary>
    /// Validates that the coupon code format is valid.
    /// </summary>
    public static void AgainstInvalidCouponCode(this string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !Utilities.GeneratedRegexPatterns.CouponCode().IsMatch(value))
        {
            throw new ArgumentException($"{parameterName} is not a valid coupon code format.", parameterName);
        }
    }

    #endregion

    #region Expiration & Activation

    /// <summary>
    /// Validates that the expiration date has not passed (expirationDate >= DateTime.UtcNow).
    /// Useful for tokens, coupons, subscriptions, and licenses.
    /// </summary>
    public static void AgainstExpired(this DateTime expirationDate, string parameterName)
    {
        if (expirationDate < DateTime.UtcNow)
        {
            throw new ArgumentException($"{parameterName} has expired.", parameterName);
        }
    }

    /// <summary>
    /// Validates that the activation date has already passed (activationDate &lt;= DateTime.UtcNow).
    /// Ensures the resource is already active and available for use.
    /// </summary>
    public static void AgainstNotYetActive(this DateTime activationDate, string parameterName)
    {
        if (activationDate > DateTime.UtcNow)
        {
            throw new ArgumentException($"{parameterName} is not yet active.", parameterName);
        }
    }

    #endregion

    #region Rating & Review

    /// <summary>
    /// Validates that the rating is within valid range.
    /// </summary>
    public static void AgainstInvalidRating(this int rating, string parameterName, int minRating = 1, int maxRating = 5)
    {
        if (rating < minRating || rating > maxRating)
        {
            throw new ArgumentOutOfRangeException(parameterName, $"{parameterName} must be between {minRating} and {maxRating}.");
        }
    }

    /// <summary>
    /// Validates that the review text meets requirements.
    /// </summary>
    public static void AgainstInvalidReviewText(this string value, string parameterName, int minLength = 10, int maxLength = 5000)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length < minLength || value.Length > maxLength)
        {
            throw new ArgumentException($"{parameterName} must be between {minLength} and {maxLength} characters.", parameterName);
        }
    }

    #endregion
}
