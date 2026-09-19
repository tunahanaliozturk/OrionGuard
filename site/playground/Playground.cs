using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Moongazing.OrionGuard.Core;
using Moongazing.OrionGuard.DynamicRules;
using Moongazing.OrionGuard.Extensions;

namespace OrionGuard.Playground;

/// <summary>
/// The type the input JSON is read into. <see cref="DynamicValidator"/> reads properties of the
/// validated object by reflection, so the input needs a concrete CLR type; a rule that names
/// anything else is skipped without an error.
/// </summary>
public sealed class PlaygroundInput
{
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? Password { get; set; }
    public string? Website { get; set; }
    public string? Country { get; set; }
    public string? TaxNumber { get; set; }
    public int? Age { get; set; }
    public decimal? Amount { get; set; }
    public bool? IsCompany { get; set; }
}

/// <summary>Result of one run of the dynamic rule engine.</summary>
public sealed record RulesOutcome(
    GuardResult? Result,
    string? Error,
    IReadOnlyList<string> Warnings)
{
    public static RulesOutcome Failed(string error) => new(null, error, []);
}

/// <summary>Runs <see cref="DynamicValidator.FromJson(string)"/> over a JSON input object.</summary>
public static class DynamicRulesRunner
{
    // The input is strict JSON, and a property the model does not have is reported instead of
    // being dropped silently, so a typo in the input does not look like a passing rule.
    private static readonly JsonSerializerOptions InputOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    // Same options DynamicValidator.FromJson uses, for the second read that produces the warnings.
    private static readonly JsonSerializerOptions RuleSetOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // The rule types DynamicValidator.EvaluateRule understands. Anything else is skipped without
    // an error, so the playground warns about it. Keep in sync with DynamicValidator.
    private static readonly HashSet<string> KnownRuleTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "NotNull", "Required", "NotEmpty", "Length", "MinLength", "MaxLength", "Range",
        "GreaterThan", "LessThan", "Regex", "Pattern", "Email", "Url", "In", "NotIn"
    };

    public static RulesOutcome Run(string rulesJson, string inputJson)
    {
        if (string.IsNullOrWhiteSpace(rulesJson))
        {
            return RulesOutcome.Failed("Rules: the rule set is empty.");
        }

        DynamicValidator validator;
        try
        {
            validator = DynamicValidator.FromJson(rulesJson);
        }
        catch (Exception ex)
        {
            return RulesOutcome.Failed($"Rules: {Describe(ex)}");
        }

        PlaygroundInput? input;
        try
        {
            input = JsonSerializer.Deserialize<PlaygroundInput>(inputJson, InputOptions);
        }
        catch (Exception ex)
        {
            return RulesOutcome.Failed($"Input: {Describe(ex)}");
        }

        if (input is null)
        {
            return RulesOutcome.Failed("Input: expected a JSON object.");
        }

        var warnings = Warnings(rulesJson);

        try
        {
            return new RulesOutcome(validator.Validate(input), null, warnings);
        }
        catch (Exception ex)
        {
            // A rule the engine cannot evaluate at all leaves Validate as an exception instead of
            // producing an error, so the page reports it rather than tearing down the component.
            return new RulesOutcome(null, $"Validate: {Describe(ex)}", warnings);
        }
    }

    /// <summary>
    /// Rules that the engine will quietly ignore: an unknown rule type, or a property name that
    /// does not exist on <see cref="PlaygroundInput"/> (the lookup is case-sensitive). This pass is
    /// advisory, so it never throws: an unexpected shape here must not replace the validation
    /// result with an unhandled exception in the Blazor event handler.
    /// </summary>
    private static List<string> Warnings(string rulesJson)
    {
        var warnings = new List<string>();

        try
        {
            var ruleSet = JsonSerializer.Deserialize<DynamicRuleSet>(rulesJson, RuleSetOptions);
            if (ruleSet?.Rules is null)
            {
                return warnings;
            }

            for (var i = 0; i < ruleSet.Rules.Count; i++)
            {
                var rule = ruleSet.Rules[i];
                var label = $"Rule {i + 1}";

                // A JSON null in the rules array deserializes to a null element.
                if (rule is null)
                {
                    warnings.Add($"{label}: the entry is null, so it carries nothing to validate.");
                    continue;
                }

                // Every string on DynamicRule is assignable to null from JSON, whatever its initializer says.
                var ruleType = rule.RuleType ?? string.Empty;
                var propertyName = rule.PropertyName ?? string.Empty;

                if (!KnownRuleTypes.Contains(ruleType))
                {
                    warnings.Add($"{label}: rule type '{ruleType}' is not one the engine knows, so the rule is skipped.");
                }

                if (Property(propertyName) is null)
                {
                    warnings.Add($"{label}: '{propertyName}' is not a property of the input model, so the rule is skipped.{Hint(propertyName)}");
                }

                if (!string.IsNullOrEmpty(rule.WhenProperty) && Property(rule.WhenProperty) is null)
                {
                    warnings.Add($"{label}: the condition property '{rule.WhenProperty}' does not exist, so the condition is ignored and the rule always runs.{Hint(rule.WhenProperty)}");
                }
            }
        }
        catch (Exception)
        {
            // The rules already parsed once, for FromJson. Anything unexpected here belongs to the
            // inspection alone, and the validation result below is still worth showing.
        }

        return warnings;
    }

    private static PropertyInfo? Property(string name) =>
        string.IsNullOrEmpty(name)
            ? null
            : typeof(PlaygroundInput).GetProperty(name, BindingFlags.Public | BindingFlags.Instance);

    private static string Hint(string name)
    {
        var match = typeof(PlaygroundInput)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        return match is null ? string.Empty : $" Did you mean '{match.Name}'?";
    }

    private static string Describe(Exception ex) => $"{ex.GetType().Name}: {ex.Message}";

    public static IReadOnlyList<string> ModelProperties { get; } =
        typeof(PlaygroundInput)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => $"{p.Name}: {FriendlyType(p.PropertyType)}")
            .ToArray();

    private static string FriendlyType(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        var name = underlying == typeof(string) ? "string"
            : underlying == typeof(int) ? "int"
            : underlying == typeof(decimal) ? "decimal"
            : underlying == typeof(bool) ? "bool"
            : underlying.Name;
        return underlying == type && type != typeof(string) ? name : name + "?";
    }
}

/// <summary>One guard the playground runs against the value in the input box.</summary>
public sealed record GuardCheck(string Name, string Call, Action<string, string[]> Run);

/// <summary>What a guard did with the value.</summary>
public enum GuardVerdict
{
    /// <summary>The guard returned without throwing.</summary>
    Passes,

    /// <summary>The guard threw, which is how it rejects a value.</summary>
    Rejects,

    /// <summary>The guard cannot run in this environment; see <see cref="GuardOutcome.Message"/>.</summary>
    Unavailable
}

/// <summary>One guard's answer for the current value.</summary>
public sealed record GuardOutcome(GuardCheck Check, GuardVerdict Verdict, string? Message, string? ExceptionType);

public static class GuardRunner
{
    public static readonly GuardCheck[] Checks =
    [
        new("Email", "Ensure.That(input).Email()",
            static (input, _) => Ensure.That(input).Email()),

        new("URL", "Ensure.That(input).Url()",
            static (input, _) => Ensure.That(input).Url()),

        new("Open redirect", "input.AgainstOpenRedirect(nameof(input), allowedDomains)",
            static (input, allowedDomains) => input.AgainstOpenRedirect(nameof(input), allowedDomains)),

        new("Path traversal", "input.AgainstPathTraversal(nameof(input))",
            static (input, _) => input.AgainstPathTraversal(nameof(input))),

        new("SQL injection (heuristic)", "input.AgainstSqlInjection(nameof(input))",
            static (input, _) => input.AgainstSqlInjection(nameof(input))),

        new("XSS (heuristic)", "input.AgainstXss(nameof(input))",
            static (input, _) => input.AgainstXss(nameof(input)))
    ];

    public static List<GuardOutcome> RunAll(string input, string[] allowedDomains)
    {
        var outcomes = new List<GuardOutcome>(Checks.Length);

        foreach (var check in Checks)
        {
            try
            {
                check.Run(input, allowedDomains);
                outcomes.Add(new GuardOutcome(check, GuardVerdict.Passes, Message: null, ExceptionType: null));
            }
            catch (PlatformNotSupportedException ex)
            {
                // .NET on WebAssembly has no NFKC normalization, which AgainstPathTraversal applies
                // before it looks for traversal sequences, so a non-ASCII value makes it throw here.
                // On a server or desktop runtime the same call returns an answer.
                outcomes.Add(new GuardOutcome(check, GuardVerdict.Unavailable, ex.Message, ex.GetType().Name));
            }
            catch (Exception ex)
            {
                outcomes.Add(new GuardOutcome(check, GuardVerdict.Rejects, ex.Message, ex.GetType().Name));
            }
        }

        return outcomes;
    }
}

/// <summary>Starting points for both panels.</summary>
public static class Samples
{
    public sealed record RuleSample(string Title, string Rules, string Input, string Note);

    public static readonly RuleSample[] RuleSamples =
    [
        new("Sign-up form",
            """
            {
              "Name": "SignUp",
              "Rules": [
                { "PropertyName": "Email", "RuleType": "NotEmpty" },
                { "PropertyName": "Email", "RuleType": "Email" },
                { "PropertyName": "Password", "RuleType": "MinLength", "Parameters": { "Min": 8 } },
                { "PropertyName": "Age", "RuleType": "Range", "Parameters": { "Min": 18, "Max": 120 } },
                { "PropertyName": "Country", "RuleType": "In", "Parameters": { "Values": ["US", "UK", "TR"] } },
                { "PropertyName": "Website", "RuleType": "Url" }
              ]
            }
            """,
            """
            {
              "Email": "not-an-email",
              "Password": "short",
              "Age": 16,
              "Country": "FR",
              "Website": "javascript:alert(1)"
            }
            """,
            "Every rule that fails adds one error; nothing throws."),

        new("Conditional rule",
            """
            {
              "Name": "Invoice",
              "Rules": [
                {
                  "PropertyName": "TaxNumber",
                  "RuleType": "NotEmpty",
                  "WhenProperty": "IsCompany",
                  "WhenValue": true,
                  "ErrorMessage": "Companies must provide a tax number.",
                  "ErrorCode": "TAX_NUMBER_REQUIRED"
                },
                { "PropertyName": "TaxNumber", "RuleType": "Regex", "Parameters": { "Pattern": "^[0-9]{10}$" } },
                { "PropertyName": "Amount", "RuleType": "GreaterThan", "Parameters": { "Value": 0 } }
              ]
            }
            """,
            """
            {
              "IsCompany": true,
              "TaxNumber": "",
              "Amount": 0
            }
            """,
            "Set IsCompany to false and the first rule stops running. ErrorMessage and ErrorCode replace the generated defaults."),

        new("Rules that are skipped",
            """
            {
              "Name": "Typos",
              "Rules": [
                { "PropertyName": "email", "RuleType": "Email" },
                { "PropertyName": "Name", "RuleType": "NotEmpy" },
                { "PropertyName": "Name", "RuleType": "MaxLength", "Parameters": { "Max": 5 } }
              ]
            }
            """,
            """
            {
              "Email": "not-an-email",
              "Name": "Ada Lovelace"
            }
            """,
            "A property name in the wrong case and an unknown rule type are both ignored without an error. Only the third rule runs.")
    ];

    public static readonly string[] GuardSamples =
    [
        "user@example.com",
        "https://example.com/welcome",
        "/account/settings",
        "//evil.example/login",
        "https://sub.example.com/ok",
        "../../etc/passwd",
        "%2e%2e%2fsecret.txt",
        "．．／secret.txt",
        "Robert'); DROP TABLE Students;--",
        "Please update my delivery address",
        "<img src=x onerror=alert(1)>",
        "jav&#x61;script:alert(1)",
        "O'Brien"
    ];
}
