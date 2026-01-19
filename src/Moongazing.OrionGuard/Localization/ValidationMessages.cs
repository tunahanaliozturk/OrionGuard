using System.Globalization;
using System.Resources;

namespace Moongazing.OrionGuard.Localization;

/// <summary>
/// Provides localization support for validation messages.
/// </summary>
public static class ValidationMessages
{
    private static Func<string, string?, string> _messageResolver = DefaultMessageResolver;
    private static CultureInfo _culture = CultureInfo.CurrentCulture;

    private static readonly Dictionary<string, Dictionary<string, string>> _messages = new()
    {
        ["en"] = new Dictionary<string, string>
        {
            ["NotNull"] = "{0} cannot be null.",
            ["NotEmpty"] = "{0} cannot be empty.",
            ["NotDefault"] = "{0} cannot be default value.",
            ["Length"] = "{0} must be between {1} and {2} characters.",
            ["MinLength"] = "{0} must be at least {1} characters.",
            ["MaxLength"] = "{0} must be at most {1} characters.",
            ["Email"] = "{0} must be a valid email address.",
            ["Url"] = "{0} must be a valid URL.",
            ["Pattern"] = "{0} does not match the required pattern.",
            ["GreaterThan"] = "{0} must be greater than {1}.",
            ["LessThan"] = "{0} must be less than {1}.",
            ["InRange"] = "{0} must be between {1} and {2}.",
            ["Positive"] = "{0} must be positive.",
            ["NotNegative"] = "{0} cannot be negative.",
            ["NotZero"] = "{0} cannot be zero.",
            ["InPast"] = "{0} must be in the past.",
            ["InFuture"] = "{0} must be in the future.",
            ["CreditCard"] = "{0} is not a valid credit card number.",
            ["Iban"] = "{0} is not a valid IBAN.",
            ["PhoneNumber"] = "{0} is not a valid phone number.",
            ["Required"] = "{0} is required.",
            ["Unique"] = "{0} must be unique.",
            ["Exists"] = "{0} does not exist."
        },
        ["tr"] = new Dictionary<string, string>
        {
            ["NotNull"] = "{0} bo? olamaz.",
            ["NotEmpty"] = "{0} bo? veya sadece bo?luk olamaz.",
            ["NotDefault"] = "{0} varsay?lan de?er olamaz.",
            ["Length"] = "{0} {1} ile {2} karakter aras?nda olmal?d?r.",
            ["MinLength"] = "{0} en az {1} karakter olmal?d?r.",
            ["MaxLength"] = "{0} en fazla {1} karakter olabilir.",
            ["Email"] = "{0} geçerli bir e-posta adresi olmal?d?r.",
            ["Url"] = "{0} geçerli bir URL olmal?d?r.",
            ["Pattern"] = "{0} gerekli desene uymuyor.",
            ["GreaterThan"] = "{0} {1} de?erinden büyük olmal?d?r.",
            ["LessThan"] = "{0} {1} de?erinden küçük olmal?d?r.",
            ["InRange"] = "{0} {1} ile {2} aras?nda olmal?d?r.",
            ["Positive"] = "{0} pozitif olmal?d?r.",
            ["NotNegative"] = "{0} negatif olamaz.",
            ["NotZero"] = "{0} s?f?r olamaz.",
            ["InPast"] = "{0} geçmi? bir tarih olmal?d?r.",
            ["InFuture"] = "{0} gelecek bir tarih olmal?d?r.",
            ["CreditCard"] = "{0} geçerli bir kredi kart? numaras? de?il.",
            ["Iban"] = "{0} geçerli bir IBAN de?il.",
            ["PhoneNumber"] = "{0} geçerli bir telefon numaras? de?il.",
            ["Required"] = "{0} gereklidir.",
            ["Unique"] = "{0} benzersiz olmal?d?r.",
            ["Exists"] = "{0} mevcut de?il."
        },
        ["de"] = new Dictionary<string, string>
        {
            ["NotNull"] = "{0} darf nicht null sein.",
            ["NotEmpty"] = "{0} darf nicht leer sein.",
            ["Email"] = "{0} muss eine gültige E-Mail-Adresse sein.",
            ["Required"] = "{0} ist erforderlich."
        },
        ["fr"] = new Dictionary<string, string>
        {
            ["NotNull"] = "{0} ne peut pas être null.",
            ["NotEmpty"] = "{0} ne peut pas être vide.",
            ["Email"] = "{0} doit être une adresse e-mail valide.",
            ["Required"] = "{0} est requis."
        }
    };

    /// <summary>
    /// Sets the culture for validation messages.
    /// </summary>
    public static void SetCulture(CultureInfo culture)
    {
        _culture = culture;
    }

    /// <summary>
    /// Sets the culture for validation messages using culture name.
    /// </summary>
    public static void SetCulture(string cultureName)
    {
        _culture = new CultureInfo(cultureName);
    }

    /// <summary>
    /// Sets a custom message resolver function.
    /// </summary>
    public static void SetMessageResolver(Func<string, string?, string> resolver)
    {
        _messageResolver = resolver;
    }

    /// <summary>
    /// Adds or updates messages for a specific culture.
    /// </summary>
    public static void AddMessages(string cultureName, Dictionary<string, string> messages)
    {
        if (!_messages.ContainsKey(cultureName))
        {
            _messages[cultureName] = new Dictionary<string, string>();
        }

        foreach (var kvp in messages)
        {
            _messages[cultureName][kvp.Key] = kvp.Value;
        }
    }

    /// <summary>
    /// Gets a localized message.
    /// </summary>
    public static string Get(string key, params object[] args)
    {
        var template = _messageResolver(key, _culture.TwoLetterISOLanguageName);
        return string.Format(template, args);
    }

    /// <summary>
    /// Gets a localized message for a specific culture.
    /// </summary>
    public static string Get(string key, CultureInfo culture, params object[] args)
    {
        var template = _messageResolver(key, culture.TwoLetterISOLanguageName);
        return string.Format(template, args);
    }

    private static string DefaultMessageResolver(string key, string? cultureName)
    {
        cultureName ??= "en";

        if (_messages.TryGetValue(cultureName, out var cultureMessages) &&
            cultureMessages.TryGetValue(key, out var message))
        {
            return message;
        }

        // Fallback to English
        if (_messages.TryGetValue("en", out var englishMessages) &&
            englishMessages.TryGetValue(key, out var englishMessage))
        {
            return englishMessage;
        }

        return key;
    }
}

/// <summary>
/// Extension methods for using localized validation messages.
/// </summary>
public static class LocalizedGuardExtensions
{
    /// <summary>
    /// Gets a localized validation message.
    /// </summary>
    public static string Localized(this string key, params object[] args)
    {
        return ValidationMessages.Get(key, args);
    }
}
