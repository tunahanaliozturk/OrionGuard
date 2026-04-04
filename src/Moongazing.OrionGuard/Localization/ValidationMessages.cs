using System.Collections.Concurrent;
using System.Globalization;

namespace Moongazing.OrionGuard.Localization;

/// <summary>
/// Provides thread-safe localization support for validation messages.
/// </summary>
public static class ValidationMessages
{
    private static volatile Func<string, string?, string> _messageResolver = DefaultMessageResolver;
    private static readonly AsyncLocal<CultureInfo?> _asyncLocalCulture = new();
    private static volatile CultureInfo _globalCulture = CultureInfo.CurrentCulture;

    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, string>> _messages = new(
        new Dictionary<string, ConcurrentDictionary<string, string>>
        {
            ["en"] = new(new Dictionary<string, string>
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
                ["Exists"] = "{0} does not exist.",
                ["SqlInjection"] = "{0} contains potentially dangerous SQL content.",
                ["Xss"] = "{0} contains potentially dangerous script content.",
                ["PathTraversal"] = "{0} contains a path traversal sequence.",
                ["WeakPassword"] = "{0} does not meet password strength requirements.",
                ["TurkishId"] = "{0} is not a valid Turkish ID number.",
                ["TaxNumber"] = "{0} is not a valid tax number.",
                ["LicensePlate"] = "{0} is not a valid license plate."
            }),
            ["tr"] = new(new Dictionary<string, string>
            {
                ["NotNull"] = "{0} boş olamaz.",
                ["NotEmpty"] = "{0} boş veya sadece boşluk olamaz.",
                ["NotDefault"] = "{0} varsayılan değer olamaz.",
                ["Length"] = "{0} {1} ile {2} karakter arasında olmalıdır.",
                ["MinLength"] = "{0} en az {1} karakter olmalıdır.",
                ["MaxLength"] = "{0} en fazla {1} karakter olabilir.",
                ["Email"] = "{0} geçerli bir e-posta adresi olmalıdır.",
                ["Url"] = "{0} geçerli bir URL olmalıdır.",
                ["Pattern"] = "{0} gerekli desene uymuyor.",
                ["GreaterThan"] = "{0} {1} değerinden büyük olmalıdır.",
                ["LessThan"] = "{0} {1} değerinden küçük olmalıdır.",
                ["InRange"] = "{0} {1} ile {2} arasında olmalıdır.",
                ["Positive"] = "{0} pozitif olmalıdır.",
                ["NotNegative"] = "{0} negatif olamaz.",
                ["NotZero"] = "{0} sıfır olamaz.",
                ["InPast"] = "{0} geçmiş bir tarih olmalıdır.",
                ["InFuture"] = "{0} gelecek bir tarih olmalıdır.",
                ["CreditCard"] = "{0} geçerli bir kredi kartı numarası değil.",
                ["Iban"] = "{0} geçerli bir IBAN değil.",
                ["PhoneNumber"] = "{0} geçerli bir telefon numarası değil.",
                ["Required"] = "{0} gereklidir.",
                ["Unique"] = "{0} benzersiz olmalıdır.",
                ["Exists"] = "{0} mevcut değil.",
                ["SqlInjection"] = "{0} tehlikeli SQL içeriği barındırıyor.",
                ["Xss"] = "{0} tehlikeli script içeriği barındırıyor.",
                ["PathTraversal"] = "{0} dizin geçiş dizisi içeriyor.",
                ["WeakPassword"] = "{0} şifre güvenlik gereksinimlerini karşılamıyor.",
                ["TurkishId"] = "{0} geçerli bir TC Kimlik Numarası değil.",
                ["TaxNumber"] = "{0} geçerli bir vergi numarası değil.",
                ["LicensePlate"] = "{0} geçerli bir plaka değil."
            }),
            ["de"] = new(new Dictionary<string, string>
            {
                ["NotNull"] = "{0} darf nicht null sein.",
                ["NotEmpty"] = "{0} darf nicht leer sein.",
                ["NotDefault"] = "{0} darf nicht der Standardwert sein.",
                ["Length"] = "{0} muss zwischen {1} und {2} Zeichen lang sein.",
                ["MinLength"] = "{0} muss mindestens {1} Zeichen lang sein.",
                ["MaxLength"] = "{0} darf höchstens {1} Zeichen lang sein.",
                ["Email"] = "{0} muss eine gültige E-Mail-Adresse sein.",
                ["Url"] = "{0} muss eine gültige URL sein.",
                ["Pattern"] = "{0} entspricht nicht dem erforderlichen Muster.",
                ["GreaterThan"] = "{0} muss größer als {1} sein.",
                ["LessThan"] = "{0} muss kleiner als {1} sein.",
                ["InRange"] = "{0} muss zwischen {1} und {2} liegen.",
                ["Positive"] = "{0} muss positiv sein.",
                ["NotNegative"] = "{0} darf nicht negativ sein.",
                ["NotZero"] = "{0} darf nicht null sein.",
                ["Required"] = "{0} ist erforderlich."
            }),
            ["fr"] = new(new Dictionary<string, string>
            {
                ["NotNull"] = "{0} ne peut pas être null.",
                ["NotEmpty"] = "{0} ne peut pas être vide.",
                ["NotDefault"] = "{0} ne peut pas être la valeur par défaut.",
                ["Length"] = "{0} doit contenir entre {1} et {2} caractères.",
                ["MinLength"] = "{0} doit contenir au moins {1} caractères.",
                ["MaxLength"] = "{0} doit contenir au plus {1} caractères.",
                ["Email"] = "{0} doit être une adresse e-mail valide.",
                ["Url"] = "{0} doit être une URL valide.",
                ["Pattern"] = "{0} ne correspond pas au modèle requis.",
                ["GreaterThan"] = "{0} doit être supérieur à {1}.",
                ["LessThan"] = "{0} doit être inférieur à {1}.",
                ["InRange"] = "{0} doit être compris entre {1} et {2}.",
                ["Positive"] = "{0} doit être positif.",
                ["NotNegative"] = "{0} ne peut pas être négatif.",
                ["NotZero"] = "{0} ne peut pas être zéro.",
                ["Required"] = "{0} est requis."
            }),
            ["es"] = new(new Dictionary<string, string>
            {
                ["NotNull"] = "{0} no puede ser nulo.",
                ["NotEmpty"] = "{0} no puede estar vacío.",
                ["Email"] = "{0} debe ser una dirección de correo válida.",
                ["Required"] = "{0} es obligatorio.",
                ["InRange"] = "{0} debe estar entre {1} y {2}.",
                ["Positive"] = "{0} debe ser positivo."
            }),
            ["pt"] = new(new Dictionary<string, string>
            {
                ["NotNull"] = "{0} não pode ser nulo.",
                ["NotEmpty"] = "{0} não pode estar vazio.",
                ["Email"] = "{0} deve ser um endereço de e-mail válido.",
                ["Required"] = "{0} é obrigatório.",
                ["InRange"] = "{0} deve estar entre {1} e {2}.",
                ["Positive"] = "{0} deve ser positivo."
            }),
            ["ar"] = new(new Dictionary<string, string>
            {
                ["NotNull"] = "{0} لا يمكن أن يكون فارغاً.",
                ["NotEmpty"] = "{0} لا يمكن أن يكون خالياً.",
                ["Email"] = "{0} يجب أن يكون عنوان بريد إلكتروني صالح.",
                ["Required"] = "{0} مطلوب.",
                ["InRange"] = "{0} يجب أن يكون بين {1} و {2}.",
                ["Positive"] = "{0} يجب أن يكون إيجابياً."
            }),
            ["ja"] = new(new Dictionary<string, string>
            {
                ["NotNull"] = "{0} はnullにできません。",
                ["NotEmpty"] = "{0} は空にできません。",
                ["Email"] = "{0} は有効なメールアドレスである必要があります。",
                ["Required"] = "{0} は必須です。",
                ["InRange"] = "{0} は{1}から{2}の間である必要があります。",
                ["Positive"] = "{0} は正の数である必要があります。"
            }),
            ["it"] = new(new Dictionary<string, string>
            {
                ["NotNull"] = "{0} non può essere nullo.",
                ["NotEmpty"] = "{0} non può essere vuoto.",
                ["NotDefault"] = "{0} non può essere il valore predefinito.",
                ["Length"] = "{0} deve essere compreso tra {1} e {2} caratteri.",
                ["MinLength"] = "{0} deve contenere almeno {1} caratteri.",
                ["MaxLength"] = "{0} deve contenere al massimo {1} caratteri.",
                ["Email"] = "{0} deve essere un indirizzo email valido.",
                ["Url"] = "{0} deve essere un URL valido.",
                ["Pattern"] = "{0} non corrisponde al formato richiesto.",
                ["GreaterThan"] = "{0} deve essere maggiore di {1}.",
                ["LessThan"] = "{0} deve essere minore di {1}.",
                ["InRange"] = "{0} deve essere compreso tra {1} e {2}.",
                ["Positive"] = "{0} deve essere positivo.",
                ["NotNegative"] = "{0} non può essere negativo.",
                ["NotZero"] = "{0} non può essere zero.",
                ["InPast"] = "{0} deve essere una data passata.",
                ["InFuture"] = "{0} deve essere una data futura.",
                ["CreditCard"] = "{0} non è un numero di carta di credito valido.",
                ["Iban"] = "{0} non è un IBAN valido.",
                ["PhoneNumber"] = "{0} non è un numero di telefono valido.",
                ["Required"] = "{0} è obbligatorio.",
                ["Unique"] = "{0} deve essere univoco.",
                ["Exists"] = "{0} non esiste.",
                ["SqlInjection"] = "{0} contiene contenuto SQL potenzialmente pericoloso.",
                ["Xss"] = "{0} contiene codice script potenzialmente pericoloso.",
                ["PathTraversal"] = "{0} contiene una sequenza di path traversal.",
                ["WeakPassword"] = "{0} non soddisfa i requisiti di complessità della password.",
                ["TurkishId"] = "{0} non è un numero di documento turco valido.",
                ["TaxNumber"] = "{0} non è un numero di identificazione fiscale valido.",
                ["LicensePlate"] = "{0} non è una targa valida."
            })
        });

    /// <summary>
    /// Gets the current effective culture (async-local takes priority over global).
    /// </summary>
    public static CultureInfo CurrentCulture => _asyncLocalCulture.Value ?? _globalCulture;

    /// <summary>
    /// Sets the culture globally for validation messages.
    /// </summary>
    public static void SetCulture(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        _globalCulture = culture;
    }

    /// <summary>
    /// Sets the culture globally using culture name.
    /// </summary>
    public static void SetCulture(string cultureName)
    {
        ArgumentNullException.ThrowIfNull(cultureName);
        _globalCulture = new CultureInfo(cultureName);
    }

    /// <summary>
    /// Sets the culture for the current async context (thread-safe, does not affect other threads).
    /// </summary>
    public static void SetCultureForCurrentScope(CultureInfo culture)
    {
        _asyncLocalCulture.Value = culture;
    }

    /// <summary>
    /// Sets a custom message resolver function.
    /// </summary>
    public static void SetMessageResolver(Func<string, string?, string> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        _messageResolver = resolver;
    }

    /// <summary>
    /// Adds or updates messages for a specific culture (thread-safe).
    /// </summary>
    public static void AddMessages(string cultureName, Dictionary<string, string> messages)
    {
        var cultureMessages = _messages.GetOrAdd(cultureName, _ => new ConcurrentDictionary<string, string>());
        foreach (var kvp in messages)
        {
            cultureMessages[kvp.Key] = kvp.Value;
        }
    }

    /// <summary>
    /// Gets a localized message using the current culture.
    /// </summary>
    public static string Get(string key, params object[] args)
    {
        var template = _messageResolver(key, CurrentCulture.TwoLetterISOLanguageName);
        return string.Format(CurrentCulture, template, args);
    }

    /// <summary>
    /// Gets a localized message for a specific culture.
    /// </summary>
    public static string Get(string key, CultureInfo culture, params object[] args)
    {
        var template = _messageResolver(key, culture.TwoLetterISOLanguageName);
        return string.Format(culture, template, args);
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
