namespace Moongazing.OrionGuard.DynamicRules;

/// <summary>
/// A named set of dynamic validation rules for a specific type.
/// </summary>
public sealed class DynamicRuleSet
{
    /// <summary>Name of this rule set (e.g., "CreateUser", "UpdateOrder").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Target type name (used for documentation/matching).</summary>
    public string? TargetType { get; set; }

    /// <summary>The validation rules in this set.</summary>
    public List<DynamicRule> Rules { get; set; } = new();
}
