namespace Moongazing.OrionGuard.Profiles;

/// <summary>
/// Pre-built validation profiles for common business scenarios.
/// </summary>
public static class CommonProfiles
{
    #region User Authentication

    /// <summary>
    /// Validates an email address for authentication.
    /// </summary>
    public static Core.GuardResult Email(string? value, string parameterName = "Email")
    {
        return Core.Ensure.Accumulate(value, parameterName)
            .NotNull()
            .NotEmpty()
            .Email()
            .MaxLength(254)
            .ToResult();
    }

    /// <summary>
    /// Validates a password with configurable requirements.
    /// </summary>
    public static Core.GuardResult Password(
        string? value,
        int minLength = 8,
        int maxLength = 128,
        bool requireUppercase = true,
        bool requireLowercase = true,
        bool requireDigit = true,
        bool requireSpecialChar = true,
        string parameterName = "Password")
    {
        var guard = Core.Ensure.Accumulate(value, parameterName)
            .NotNull()
            .NotEmpty()
            .Length(minLength, maxLength);

        if (requireUppercase)
            guard = guard.Must(v => v is string s && s.Any(char.IsUpper), $"{parameterName} must contain at least one uppercase letter.");

        if (requireLowercase)
            guard = guard.Must(v => v is string s && s.Any(char.IsLower), $"{parameterName} must contain at least one lowercase letter.");

        if (requireDigit)
            guard = guard.Must(v => v is string s && s.Any(char.IsDigit), $"{parameterName} must contain at least one digit.");

        if (requireSpecialChar)
            guard = guard.Must(v => v is string s && s.Any(c => !char.IsLetterOrDigit(c)), $"{parameterName} must contain at least one special character.");

        return guard.ToResult();
    }

    /// <summary>
    /// Validates a username.
    /// </summary>
    public static Core.GuardResult Username(
        string? value,
        int minLength = 3,
        int maxLength = 30,
        string parameterName = "Username")
    {
        return Core.Ensure.Accumulate(value, parameterName)
            .NotNull()
            .NotEmpty()
            .Length(minLength, maxLength)
            .Matches(@"^[a-zA-Z0-9_]+$", $"{parameterName} can only contain letters, numbers, and underscores.")
            .ToResult();
    }

    #endregion

    #region Personal Information

    /// <summary>
    /// Validates a person's name.
    /// </summary>
    public static Core.GuardResult PersonName(
        string? value,
        int minLength = 2,
        int maxLength = 100,
        string parameterName = "Name")
    {
        return Core.Ensure.Accumulate(value, parameterName)
            .NotNull()
            .NotEmpty()
            .Length(minLength, maxLength)
            .Must(v => v is string s && !s.Any(char.IsDigit), $"{parameterName} cannot contain numbers.")
            .ToResult();
    }

    /// <summary>
    /// Validates an age value.
    /// </summary>
    public static Core.GuardResult Age(int value, int minAge = 0, int maxAge = 150, string parameterName = "Age")
    {
        return Core.Ensure.Accumulate(value, parameterName)
            .NotNegative()
            .InRange(minAge, maxAge)
            .ToResult();
    }

    /// <summary>
    /// Validates a birth date.
    /// </summary>
    public static Core.GuardResult BirthDate(DateTime value, int minAge = 0, int maxAge = 150, string parameterName = "BirthDate")
    {
        var now = DateTime.Now;
        var minDate = now.AddYears(-maxAge);
        var maxDate = now.AddYears(-minAge);

        return Core.Ensure.Accumulate(value, parameterName)
            .InPast()
            .DateBetween(minDate, maxDate, $"{parameterName} indicates an unrealistic age.")
            .ToResult();
    }

    #endregion

    #region Contact Information

    /// <summary>
    /// Validates a phone number in E.164 format.
    /// </summary>
    public static Core.GuardResult PhoneNumber(string? value, string parameterName = "PhoneNumber")
    {
        return Core.Ensure.Accumulate(value, parameterName)
            .NotNull()
            .NotEmpty()
            .Matches(@"^\+?[1-9]\d{1,14}$")
            .ToResult();
    }

    /// <summary>
    /// Validates a URL.
    /// </summary>
    public static Core.GuardResult Url(string? value, string parameterName = "Url")
    {
        return Core.Ensure.Accumulate(value, parameterName)
            .NotNull()
            .NotEmpty()
            .Url()
            .MaxLength(2048)
            .ToResult();
    }

    #endregion

    #region Financial

    /// <summary>
    /// Validates a monetary amount.
    /// </summary>
    public static Core.GuardResult MonetaryAmount(
        decimal value,
        decimal? min = null,
        decimal? max = null,
        string parameterName = "Amount")
    {
        var guard = Core.Ensure.Accumulate(value, parameterName)
            .NotNegative();

        if (min.HasValue)
            guard = guard.Must(v => v is decimal d && d >= min.Value, $"{parameterName} must be at least {min.Value}.");

        if (max.HasValue)
            guard = guard.Must(v => v is decimal d && d <= max.Value, $"{parameterName} cannot exceed {max.Value}.");

        return guard.ToResult();
    }

    /// <summary>
    /// Validates a percentage value (0-100).
    /// </summary>
    public static Core.GuardResult Percentage(decimal value, string parameterName = "Percentage")
    {
        return Core.Ensure.Accumulate(value, parameterName)
            .InRange(0m, 100m)
            .ToResult();
    }

    #endregion

    #region Identifiers

    /// <summary>
    /// Validates a GUID.
    /// </summary>
    public static Core.GuardResult GuidId(Guid value, string parameterName = "Id")
    {
        return Core.Ensure.Accumulate(value, parameterName)
            .Must(v => v is Guid g && g != Guid.Empty, $"{parameterName} cannot be empty.")
            .ToResult();
    }

    /// <summary>
    /// Validates a positive integer ID.
    /// </summary>
    public static Core.GuardResult IntegerId(int value, string parameterName = "Id")
    {
        return Core.Ensure.Accumulate(value, parameterName)
            .Positive()
            .ToResult();
    }

    /// <summary>
    /// Validates a slug (URL-friendly identifier).
    /// </summary>
    public static Core.GuardResult Slug(string? value, string parameterName = "Slug")
    {
        return Core.Ensure.Accumulate(value, parameterName)
            .NotNull()
            .NotEmpty()
            .Length(1, 200)
            .Matches(@"^[a-z0-9]+(?:-[a-z0-9]+)*$", $"{parameterName} must be a valid URL slug.")
            .ToResult();
    }

    #endregion

    #region Collections

    /// <summary>
    /// Validates a non-empty list.
    /// </summary>
    public static Core.GuardResult NonEmptyList<T>(IEnumerable<T>? value, string parameterName = "List")
    {
        return Core.Ensure.Accumulate(value, parameterName)
            .NotNull()
            .NotEmpty()
            .ToResult();
    }

    /// <summary>
    /// Validates a list with count constraints.
    /// </summary>
    public static Core.GuardResult ListWithCount<T>(
        IEnumerable<T>? value,
        int? minCount = null,
        int? maxCount = null,
        string parameterName = "List")
    {
        var guard = Core.Ensure.Accumulate(value, parameterName)
            .NotNull();

        if (minCount.HasValue)
            guard = guard.MinCount(minCount.Value);

        if (maxCount.HasValue)
            guard = guard.MaxCount(maxCount.Value);

        return guard.ToResult();
    }

    #endregion
}
