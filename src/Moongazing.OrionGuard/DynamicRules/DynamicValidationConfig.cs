namespace Moongazing.OrionGuard.DynamicRules;

/// <summary>
/// Root configuration object for dynamic validation rules.
/// Can be deserialized from JSON configuration.
/// </summary>
public sealed class DynamicValidationConfig
{
    /// <summary>All rule sets defined in this configuration.</summary>
    public List<DynamicRuleSet> RuleSets { get; set; } = new();
}
