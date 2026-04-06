namespace Moongazing.OrionGuard.DynamicRules;

/// <summary>
/// Represents a single validation rule defined in configuration.
/// </summary>
public sealed class DynamicRule
{
    /// <summary>Property name on the target object to validate.</summary>
    public string PropertyName { get; set; } = string.Empty;

    /// <summary>
    /// Rule type: NotNull, NotEmpty, Length, Range, Regex, Email, Required,
    /// MinLength, MaxLength, GreaterThan, LessThan, In, NotIn, Url, Pattern.
    /// </summary>
    public string RuleType { get; set; } = string.Empty;

    /// <summary>Custom error message. If null, a default is generated.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Error code for programmatic handling.</summary>
    public string? ErrorCode { get; set; }

    /// <summary>
    /// Parameters for the rule (e.g., Min/Max for Range, Pattern for Regex, Values for In/NotIn).
    /// </summary>
    public Dictionary<string, object> Parameters { get; set; } = new();

    /// <summary>Condition for when this rule should apply (property name that must be truthy).</summary>
    public string? WhenProperty { get; set; }

    /// <summary>Expected value of the WhenProperty for this rule to apply.</summary>
    public object? WhenValue { get; set; }
}
