namespace Moongazing.OrionGuard.DynamicRules;

/// <summary>
/// Root configuration object for dynamic validation rules.
/// Can be deserialized from JSON configuration.
/// </summary>
public sealed class DynamicValidationConfig
{
    /// <summary>All rule sets defined in this configuration. A null assignment leaves it empty.</summary>
    public List<DynamicRuleSet> RuleSets
    {
        get => ruleSets;
        set => ruleSets = value ?? new();
    }

    private List<DynamicRuleSet> ruleSets = new();
}
