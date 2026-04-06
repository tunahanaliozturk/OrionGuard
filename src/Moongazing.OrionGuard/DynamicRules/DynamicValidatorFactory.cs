using System.Collections.Concurrent;
using System.Text.Json;
using Moongazing.OrionGuard.Core;

namespace Moongazing.OrionGuard.DynamicRules;

/// <summary>
/// Thread-safe factory and registry for <see cref="DynamicValidator"/> instances.
/// Caches validators by name for efficient repeated lookups — ideal for multi-tenant
/// scenarios where each tenant has its own validation rule set.
/// </summary>
public sealed class DynamicValidatorFactory
{
    private static readonly JsonSerializerOptions DefaultJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ConcurrentDictionary<string, DynamicValidator> _validators = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers a <see cref="DynamicValidator"/> for the given name, built from the supplied rule set.
    /// Overwrites any existing registration with the same name.
    /// </summary>
    public void Register(string name, DynamicRuleSet ruleSet)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(ruleSet);

        _validators[name] = new DynamicValidator(ruleSet);
    }

    /// <summary>
    /// Registers a <see cref="DynamicValidator"/> for the given name by deserializing
    /// a JSON string that represents a single <see cref="DynamicRuleSet"/>.
    /// Overwrites any existing registration with the same name.
    /// </summary>
    public void RegisterFromJson(string name, string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        var ruleSet = JsonSerializer.Deserialize<DynamicRuleSet>(json, DefaultJsonOptions)
            ?? throw new ArgumentException("Invalid JSON rule set.", nameof(json));

        _validators[name] = new DynamicValidator(ruleSet);
    }

    /// <summary>
    /// Registers all rule sets from a <see cref="DynamicValidationConfig"/>.
    /// Each rule set is registered under its <see cref="DynamicRuleSet.Name"/>.
    /// Overwrites any existing registrations with the same names.
    /// </summary>
    public void LoadConfig(DynamicValidationConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        foreach (var ruleSet in config.RuleSets)
        {
            if (string.IsNullOrWhiteSpace(ruleSet.Name))
                continue;

            _validators[ruleSet.Name] = new DynamicValidator(ruleSet);
        }
    }

    /// <summary>
    /// Deserializes a JSON string representing a <see cref="DynamicValidationConfig"/>
    /// and registers all contained rule sets.
    /// </summary>
    public void LoadFromJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        var config = JsonSerializer.Deserialize<DynamicValidationConfig>(json, DefaultJsonOptions)
            ?? throw new ArgumentException("Invalid JSON configuration.", nameof(json));

        LoadConfig(config);
    }

    /// <summary>
    /// Retrieves a previously registered <see cref="DynamicValidator"/> by name.
    /// </summary>
    /// <exception cref="KeyNotFoundException">
    /// Thrown when no validator is registered with the specified name.
    /// </exception>
    public DynamicValidator Get(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return _validators.TryGetValue(name, out var validator)
            ? validator
            : throw new KeyNotFoundException($"No validator registered with name '{name}'.");
    }

    /// <summary>
    /// Validates an object against the named rule set.
    /// Convenience method equivalent to <c>Get(ruleSetName).Validate(instance)</c>.
    /// </summary>
    public GuardResult Validate<T>(string ruleSetName, T instance) where T : class
    {
        return Get(ruleSetName).Validate(instance);
    }
}
