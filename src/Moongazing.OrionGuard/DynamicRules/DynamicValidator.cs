using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Moongazing.OrionGuard.Core;

namespace Moongazing.OrionGuard.DynamicRules;

/// <summary>
/// Validates objects against a <see cref="DynamicRuleSet"/> using reflection.
/// Rules are defined declaratively (typically via JSON) and evaluated at runtime.
/// </summary>
public sealed class DynamicValidator
{
    private static readonly JsonSerializerOptions DefaultJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly ConcurrentDictionary<(Type, string), PropertyInfo?> _propertyCache = new();

    private readonly DynamicRuleSet _ruleSet;

    /// <summary>
    /// Initializes a new <see cref="DynamicValidator"/> with the given rule set.
    /// </summary>
    public DynamicValidator(DynamicRuleSet ruleSet)
    {
        ArgumentNullException.ThrowIfNull(ruleSet);
        _ruleSet = ruleSet;
    }

    /// <summary>
    /// Creates a <see cref="DynamicValidator"/> from a JSON string representing a single <see cref="DynamicRuleSet"/>.
    /// </summary>
    public static DynamicValidator FromJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        var ruleSet = JsonSerializer.Deserialize<DynamicRuleSet>(json, DefaultJsonOptions)
            ?? throw new ArgumentException("Invalid JSON rule set.", nameof(json));

        return new DynamicValidator(ruleSet);
    }

    /// <summary>
    /// Creates a <see cref="DynamicValidator"/> by looking up a named rule set from a <see cref="DynamicValidationConfig"/>.
    /// </summary>
    public static DynamicValidator FromConfig(DynamicValidationConfig config, string ruleSetName)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleSetName);

        var ruleSet = config.RuleSets.FirstOrDefault(r =>
                r.Name.Equals(ruleSetName, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Rule set '{ruleSetName}' not found.", nameof(ruleSetName));

        return new DynamicValidator(ruleSet);
    }

    /// <summary>
    /// Validates an object against the dynamic rules.
    /// Returns <see cref="GuardResult.Success"/> when all rules pass,
    /// or a combined <see cref="GuardResult"/> containing every violation.
    /// </summary>
    public GuardResult Validate<T>(T instance) where T : class
    {
        ArgumentNullException.ThrowIfNull(instance);

        var errors = new List<ValidationError>();
        var type = typeof(T);

        foreach (var rule in _ruleSet.Rules)
        {
            var property = _propertyCache.GetOrAdd((type, rule.PropertyName),
                key => key.Item1.GetProperty(key.Item2, BindingFlags.Public | BindingFlags.Instance));
            if (property is null) continue;

            if (!ShouldApply(rule, instance, type)) continue;

            var value = property.GetValue(instance);
            var error = EvaluateRule(rule, value, rule.PropertyName);
            if (error is not null)
            {
                errors.Add(error);
            }
        }

        return errors.Count == 0 ? GuardResult.Success() : GuardResult.Failure(errors);
    }

    #region Condition Evaluation

    private static bool ShouldApply<T>(DynamicRule rule, T instance, Type type)
    {
        if (string.IsNullOrEmpty(rule.WhenProperty)) return true;

        var whenProp = _propertyCache.GetOrAdd((type, rule.WhenProperty),
            key => key.Item1.GetProperty(key.Item2, BindingFlags.Public | BindingFlags.Instance));
        if (whenProp is null) return true;

        var whenValue = whenProp.GetValue(instance);

        // No expected value specified — just check for truthiness.
        if (rule.WhenValue is null) return whenValue is not null;

        var expectedValue = NormalizeValue(rule.WhenValue);
        return string.Equals(whenValue?.ToString(), expectedValue?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Rule Evaluation

    private static ValidationError? EvaluateRule(DynamicRule rule, object? value, string propertyName)
    {
        var ruleType = rule.RuleType.ToUpperInvariant();

        return ruleType switch
        {
            "NOTNULL" or "REQUIRED" =>
                value is null
                    ? CreateError(rule, propertyName, $"{propertyName} is required.")
                    : null,

            "NOTEMPTY" => value switch
            {
                null => CreateError(rule, propertyName, $"{propertyName} cannot be empty."),
                string s when string.IsNullOrWhiteSpace(s) => CreateError(rule, propertyName, $"{propertyName} cannot be empty."),
                _ => null
            },

            "LENGTH" => value is string s ? ValidateLength(rule, s, propertyName) : null,

            "MINLENGTH" => value is string s2 && s2.Length < GetInt(rule, "Min")
                ? CreateError(rule, propertyName, $"{propertyName} must be at least {GetInt(rule, "Min")} characters.")
                : null,

            "MAXLENGTH" => value is string s3 && s3.Length > GetInt(rule, "Max")
                ? CreateError(rule, propertyName, $"{propertyName} must be at most {GetInt(rule, "Max")} characters.")
                : null,

            "RANGE" => ValidateRange(rule, value, propertyName),

            "GREATERTHAN" => ValidateComparison(rule, value, propertyName, ">"),

            "LESSTHAN" => ValidateComparison(rule, value, propertyName, "<"),

            "REGEX" or "PATTERN" => ValidateRegex(rule, value, propertyName),

            "EMAIL" => value is string email && !string.IsNullOrWhiteSpace(email)
                       && !Utilities.GeneratedRegexPatterns.Email().IsMatch(email)
                ? CreateError(rule, propertyName, $"{propertyName} must be a valid email address.")
                : null,

            "URL" => value is string url && !string.IsNullOrWhiteSpace(url)
                     && !Uri.TryCreate(url, UriKind.Absolute, out _)
                ? CreateError(rule, propertyName, $"{propertyName} must be a valid URL.")
                : null,

            "IN" => ValidateIn(rule, value, propertyName, mustBeIn: true),

            "NOTIN" => ValidateIn(rule, value, propertyName, mustBeIn: false),

            _ => null // Unknown rule types are silently skipped.
        };
    }

    #endregion

    #region Validation Helpers

    private static ValidationError? ValidateLength(DynamicRule rule, string value, string propertyName)
    {
        var hasMin = TryGetInt(rule, "Min", out var min);
        var hasMax = TryGetInt(rule, "Max", out var max);

        if (hasMin && value.Length < min)
        {
            return CreateError(rule, propertyName,
                $"{propertyName} must be at least {min} characters.");
        }

        if (hasMax && value.Length > max)
        {
            return CreateError(rule, propertyName,
                $"{propertyName} must be at most {max} characters.");
        }

        return null;
    }

    private static ValidationError? ValidateRange(DynamicRule rule, object? value, string propertyName)
    {
        if (value is null) return null;

        if (!TryGetDouble(value, out var numericValue))
            return null;

        var hasMin = TryGetDouble(rule, "Min", out var min);
        var hasMax = TryGetDouble(rule, "Max", out var max);

        if (hasMin && numericValue < min)
        {
            return CreateError(rule, propertyName,
                $"{propertyName} must be at least {min}.");
        }

        if (hasMax && numericValue > max)
        {
            return CreateError(rule, propertyName,
                $"{propertyName} must be at most {max}.");
        }

        return null;
    }

    private static ValidationError? ValidateComparison(
        DynamicRule rule, object? value, string propertyName, string op)
    {
        if (value is null) return null;

        if (!TryGetDouble(value, out var numericValue))
            return null;

        var paramName = op == ">" ? "Value" : "Value";
        if (!TryGetDouble(rule, "Value", out var threshold))
            return null;

        return op switch
        {
            ">" when numericValue <= threshold =>
                CreateError(rule, propertyName, $"{propertyName} must be greater than {threshold}."),
            "<" when numericValue >= threshold =>
                CreateError(rule, propertyName, $"{propertyName} must be less than {threshold}."),
            _ => null
        };
    }

    private static ValidationError? ValidateRegex(DynamicRule rule, object? value, string propertyName)
    {
        if (value is not string sv || string.IsNullOrEmpty(sv))
            return null;

        if (!rule.Parameters.TryGetValue("Pattern", out var patternObj))
            return null;

        var pattern = NormalizeValue(patternObj)?.ToString();
        if (string.IsNullOrEmpty(pattern))
            return null;

        if (!Regex.IsMatch(sv, pattern))
        {
            return CreateError(rule, propertyName,
                $"{propertyName} does not match the required pattern.");
        }

        return null;
    }

    private static ValidationError? ValidateIn(
        DynamicRule rule, object? value, string propertyName, bool mustBeIn)
    {
        if (value is null) return null;

        if (!rule.Parameters.TryGetValue("Values", out var valuesObj))
            return null;

        var allowedValues = ExtractStringList(valuesObj);
        var stringValue = value.ToString() ?? string.Empty;
        var isInList = allowedValues.Contains(stringValue, StringComparer.OrdinalIgnoreCase);

        if (mustBeIn && !isInList)
        {
            return CreateError(rule, propertyName,
                $"{propertyName} must be one of: {string.Join(", ", allowedValues)}.");
        }

        if (!mustBeIn && isInList)
        {
            return CreateError(rule, propertyName,
                $"{propertyName} must not be one of: {string.Join(", ", allowedValues)}.");
        }

        return null;
    }

    #endregion

    #region Parameter Extraction

    private static int GetInt(DynamicRule rule, string parameterName)
    {
        TryGetInt(rule, parameterName, out var result);
        return result;
    }

    private static bool TryGetInt(DynamicRule rule, string parameterName, out int value)
    {
        value = 0;
        if (!rule.Parameters.TryGetValue(parameterName, out var raw))
            return false;

        var normalized = NormalizeValue(raw);
        if (normalized is not null && int.TryParse(normalized.ToString(), out value))
            return true;

        return false;
    }

    private static bool TryGetDouble(DynamicRule rule, string parameterName, out double value)
    {
        value = 0;
        if (!rule.Parameters.TryGetValue(parameterName, out var raw))
            return false;

        var normalized = NormalizeValue(raw);
        if (normalized is not null && double.TryParse(normalized.ToString(),
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out value))
            return true;

        return false;
    }

    private static bool TryGetDouble(object? value, out double result)
    {
        result = 0;
        if (value is null) return false;

        var normalized = NormalizeValue(value);
        return normalized is not null && double.TryParse(normalized.ToString(),
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out result);
    }

    private static List<string> ExtractStringList(object valuesObj)
    {
        var result = new List<string>();

        switch (valuesObj)
        {
            case JsonElement je when je.ValueKind == JsonValueKind.Array:
                foreach (var item in je.EnumerateArray())
                {
                    var s = item.ToString();
                    if (!string.IsNullOrEmpty(s))
                        result.Add(s);
                }
                break;

            case IEnumerable<object> enumerable:
                foreach (var item in enumerable)
                {
                    var normalized = NormalizeValue(item);
                    var s = normalized?.ToString();
                    if (!string.IsNullOrEmpty(s))
                        result.Add(s);
                }
                break;

            case string csv:
                result.AddRange(csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                break;
        }

        return result;
    }

    /// <summary>
    /// Converts <see cref="JsonElement"/> values to their CLR equivalents.
    /// JSON deserialization with <c>object</c> targets produces <see cref="JsonElement"/> by default.
    /// </summary>
    private static object? NormalizeValue(object? value)
    {
        if (value is not JsonElement je) return value;

        return je.ValueKind switch
        {
            JsonValueKind.String => je.GetString(),
            JsonValueKind.Number => je.TryGetInt64(out var l) ? l : je.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Undefined => null,
            _ => je.GetRawText()
        };
    }

    #endregion

    #region Error Creation

    private static ValidationError CreateError(DynamicRule rule, string propertyName, string defaultMessage)
    {
        return new ValidationError(
            propertyName,
            rule.ErrorMessage ?? defaultMessage,
            rule.ErrorCode);
    }

    #endregion
}
