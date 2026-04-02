using Moongazing.OrionGuard.Exceptions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Moongazing.OrionGuard.Core;

public static class Guard
{
    public static GuardBuilder<T> For<T>(T value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(parameterName);
        return new GuardBuilder<T>(value, parameterName);
    }

    public static void AgainstNull<T>(T? value, string parameterName) where T : class
    {
        if (value is null)
            ThrowHelper.ThrowNullValue(parameterName);
    }

    public static void AgainstNullOrEmpty(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            ThrowHelper.ThrowEmptyString(parameterName);
    }

    public static void AgainstOutOfRange<T>(T value, T min, T max, string parameterName) where T : IComparable
    {
        if (value.CompareTo(min) < 0 || value.CompareTo(max) > 0)
            ThrowHelper.ThrowOutOfRange(parameterName, min, max);
    }

    public static void AgainstNegative(this int value, string parameterName)
    {
        if (value < 0)
            ThrowHelper.ThrowNegative(parameterName);
    }

    public static void AgainstNegativeDecimal(this decimal value, string parameterName)
    {
        if (value < 0)
            ThrowHelper.ThrowNegativeDecimal(parameterName);
    }

    public static void AgainstLessThan(this int value, int minValue, string parameterName)
    {
        if (value < minValue)
            ThrowHelper.ThrowLessThan(parameterName, minValue);
    }

    public static void AgainstGreaterThan(this int value, int maxValue, string parameterName)
    {
        if (value > maxValue)
            ThrowHelper.ThrowGreaterThan(parameterName, maxValue);
    }

    public static void AgainstOutOfRange(this int value, int minValue, int maxValue, string parameterName)
    {
        if (value < minValue || value > maxValue)
            ThrowHelper.ThrowOutOfRange(parameterName, minValue, maxValue);
    }

    public static void AgainstFalse(this bool value, string parameterName)
    {
        if (!value)
            ThrowHelper.ThrowFalse(parameterName);
    }

    public static void AgainstTrue(this bool value, string parameterName)
    {
        if (value)
            ThrowHelper.ThrowTrue(parameterName);
    }

    public static void AgainstUninitializedProperties<T>(this T obj, string parameterName)
    {
        var properties = typeof(T).GetProperties();
        foreach (var property in properties)
        {
            if (property.GetValue(obj) is null)
            {
                throw new UninitializedPropertyException(parameterName, property.Name);
            }
        }
    }

    public static void AgainstInvalidEmail(string email, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(email) ||
            !RegexCache.IsMatch(email, Utilities.RegexPatterns.Email))
            ThrowHelper.ThrowInvalidEmail(parameterName);
    }

    public static void AgainstInvalidUrl(string value, string parameterName)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uriResult) ||
            !(uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps))
            ThrowHelper.ThrowInvalidUrl(parameterName);
    }

    public static void AgainstInvalidIp(string ipAddress, string parameterName)
    {
        if (!System.Net.IPAddress.TryParse(ipAddress, out _))
            ThrowHelper.ThrowInvalidIp(parameterName);
    }

    public static void AgainstInvalidGuid(string value, string parameterName)
    {
        if (!Guid.TryParse(value, out _))
            ThrowHelper.ThrowInvalidGuid(parameterName);
    }

    public static void AgainstPastDate(DateTime date, string parameterName)
    {
        if (date < DateTime.UtcNow)
            ThrowHelper.ThrowPastDate(parameterName);
    }

    public static void AgainstFutureDate(DateTime date, string parameterName)
    {
        if (date > DateTime.UtcNow)
            ThrowHelper.ThrowFutureDate(parameterName);
    }

    public static void AgainstEmptyFile(string filePath, string parameterName)
    {
        if (!File.Exists(filePath))
            ThrowHelper.ThrowFileNotExists(parameterName);

        var fileInfo = new FileInfo(filePath);
        if (fileInfo.Length == 0)
            ThrowHelper.ThrowEmptyFile(parameterName);
    }

    public static void AgainstInvalidFileExtension(string filePath, string[] validExtensions, string parameterName)
    {
        var extension = Path.GetExtension(filePath);
        if (!validExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidFileExtensionException(
                $"{parameterName} must have one of the following extensions: {string.Join(", ", validExtensions)}.");
        }
    }

    public static void AgainstNonAlphanumericCharacters(string value, string parameterName)
    {
        if (!RegexCache.IsMatch(value, @"^[a-zA-Z0-9]+$"))
            throw new OnlyAlphanumericCharacterException(parameterName);
    }

    public static void AgainstWeakPassword(string value, string parameterName)
    {
        if (!RegexCache.IsMatch(value, @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&])[A-Za-z\d@$!%*?&]{8,}$"))
            ThrowHelper.ThrowWeakPassword(parameterName);
    }

    public static void AgainstExceedingCount<T>(IEnumerable<T> collection, int maxCount, string parameterName)
    {
        if (collection.Count() > maxCount)
        {
            throw new ExceedingCountException(parameterName, maxCount);
        }
    }

    public static void AgainstEmptyCollection<T>(IEnumerable<T> collection, string parameterName)
    {
        if (collection is null || !collection.Any())
            ThrowHelper.ThrowNullValue(parameterName);
    }

    public static void AgainstNullItems<T>(IEnumerable<T?> collection, string parameterName)
    {
        if (collection.Any(item => item is null))
            ThrowHelper.ThrowNullValue($"{parameterName} contains null items");
    }

    public static void AgainstInvalidEnum<TEnum>(TEnum value, string parameterName) where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
            ThrowHelper.ThrowInvalidEnum(parameterName);
    }

    public static void AgainstInvalidXml(string xmlContent, string parameterName)
    {
        try
        {
            System.Xml.Linq.XDocument.Parse(xmlContent);
        }
        catch (Exception)
        {
            ThrowHelper.ThrowInvalidXml(parameterName);
        }
    }

    public static void AgainstUnrealisticBirthDate(DateTime date, string parameterName)
    {
        var now = DateTime.UtcNow;
        var maxDate = now.AddYears(-120);
        if (date > now || date < maxDate)
        {
            throw new UnrealisticBirthDateException(parameterName);
        }
    }

    public static void AgainstCharactersOutsideSet(string value, string allowedCharacters, string parameterName)
    {
        if (!RegexCache.IsMatch(value, $"^[{Regex.Escape(allowedCharacters)}]+$"))
        {
            throw new CharactersOutsideSetException(parameterName);
        }
    }
}
