using System.Linq.Expressions;
using System.Reflection;

namespace Moongazing.OrionGuard.Attributes;

/// <summary>
/// Marks a property as requiring validation.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter, AllowMultiple = true)]
public abstract class ValidationAttribute : Attribute
{
    /// <summary>
    /// Custom error message. If null, a default message is used.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Error code for programmatic error handling.
    /// </summary>
    public string? ErrorCode { get; set; }

    /// <summary>
    /// Validates the value.
    /// </summary>
    public abstract bool IsValid(object? value);

    /// <summary>
    /// Gets the default error message.
    /// </summary>
    protected abstract string GetDefaultMessage(string propertyName);

    /// <summary>
    /// Gets the error message for validation failure.
    /// </summary>
    public string GetMessage(string propertyName) => ErrorMessage ?? GetDefaultMessage(propertyName);
}

/// <summary>
/// Validates that a value is not null.
/// </summary>
public sealed class NotNullAttribute : ValidationAttribute
{
    public override bool IsValid(object? value) => value is not null;
    protected override string GetDefaultMessage(string propertyName) => $"{propertyName} cannot be null.";
}

/// <summary>
/// Validates that a string is not null or empty.
/// </summary>
public sealed class NotEmptyAttribute : ValidationAttribute
{
    public override bool IsValid(object? value)
    {
        return value switch
        {
            null => false,
            string s => !string.IsNullOrWhiteSpace(s),
            System.Collections.IEnumerable e => e.Cast<object>().Any(),
            _ => true
        };
    }
    protected override string GetDefaultMessage(string propertyName) => $"{propertyName} cannot be empty.";
}

/// <summary>
/// Validates that a string has a specific length range.
/// </summary>
public sealed class LengthAttribute : ValidationAttribute
{
    public int MinLength { get; }
    public int MaxLength { get; }

    public LengthAttribute(int minLength, int maxLength)
    {
        MinLength = minLength;
        MaxLength = maxLength;
    }

    public override bool IsValid(object? value)
    {
        if (value is string s)
        {
            return s.Length >= MinLength && s.Length <= MaxLength;
        }
        return true;
    }

    protected override string GetDefaultMessage(string propertyName) =>
        $"{propertyName} must be between {MinLength} and {MaxLength} characters.";
}

/// <summary>
/// Validates that a string is a valid email address.
/// </summary>
public sealed class EmailAttribute : ValidationAttribute
{
    private static readonly System.Text.RegularExpressions.Regex EmailRegex =
        new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", System.Text.RegularExpressions.RegexOptions.Compiled);

    public override bool IsValid(object? value)
    {
        if (value is string s)
        {
            return !string.IsNullOrWhiteSpace(s) && EmailRegex.IsMatch(s);
        }
        return value is null; // Null is valid, use [NotNull] for null check
    }

    protected override string GetDefaultMessage(string propertyName) =>
        $"{propertyName} must be a valid email address.";
}

/// <summary>
/// Validates that a numeric value is within a range.
/// </summary>
public sealed class RangeAttribute : ValidationAttribute
{
    public double Minimum { get; }
    public double Maximum { get; }

    public RangeAttribute(double minimum, double maximum)
    {
        Minimum = minimum;
        Maximum = maximum;
    }

    public RangeAttribute(int minimum, int maximum)
    {
        Minimum = minimum;
        Maximum = maximum;
    }

    public override bool IsValid(object? value)
    {
        if (value is null) return true;

        try
        {
            var doubleValue = Convert.ToDouble(value);
            return doubleValue >= Minimum && doubleValue <= Maximum;
        }
        catch
        {
            return false;
        }
    }

    protected override string GetDefaultMessage(string propertyName) =>
        $"{propertyName} must be between {Minimum} and {Maximum}.";
}

/// <summary>
/// Validates that a string matches a regex pattern.
/// </summary>
public sealed class RegexAttribute : ValidationAttribute
{
    public string Pattern { get; }

    public RegexAttribute(string pattern)
    {
        Pattern = pattern;
    }

    public override bool IsValid(object? value)
    {
        if (value is string s)
        {
            return System.Text.RegularExpressions.Regex.IsMatch(s, Pattern);
        }
        return value is null;
    }

    protected override string GetDefaultMessage(string propertyName) =>
        $"{propertyName} does not match the required pattern.";
}

/// <summary>
/// Validates that a value is positive.
/// </summary>
public sealed class PositiveAttribute : ValidationAttribute
{
    public override bool IsValid(object? value)
    {
        if (value is null) return true;

        try
        {
            var doubleValue = Convert.ToDouble(value);
            return doubleValue > 0;
        }
        catch
        {
            return false;
        }
    }

    protected override string GetDefaultMessage(string propertyName) =>
        $"{propertyName} must be positive.";
}

/// <summary>
/// Validates objects using validation attributes.
/// </summary>
public static class AttributeValidator
{
    /// <summary>
    /// Validates an object using its validation attributes.
    /// </summary>
    public static Core.GuardResult Validate<T>(T instance) where T : class
    {
        var errors = new List<Core.ValidationError>();
        var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var property in properties)
        {
            var attributes = property.GetCustomAttributes<ValidationAttribute>();
            var value = property.GetValue(instance);

            foreach (var attribute in attributes)
            {
                if (!attribute.IsValid(value))
                {
                    errors.Add(new Core.ValidationError(
                        property.Name,
                        attribute.GetMessage(property.Name),
                        attribute.ErrorCode
                    ));
                }
            }
        }

        return errors.Count == 0 ? Core.GuardResult.Success() : Core.GuardResult.Failure(errors);
    }

    /// <summary>
    /// Validates an object and throws if invalid.
    /// </summary>
    public static T ValidateAndThrow<T>(T instance) where T : class
    {
        var result = Validate(instance);
        result.ThrowIfInvalid();
        return instance;
    }
}
