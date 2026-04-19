namespace Moongazing.OrionGuard.Core;

/// <summary>
/// Represents a named group of validation rules that can be executed selectively.
/// Rule sets allow organizing validation logic into logical groups (e.g., create, update, delete)
/// that can be run independently or in combination.
/// </summary>
public sealed class RuleSet
{
    /// <summary>
    /// Gets the name of this rule set.
    /// </summary>
    public string Name { get; }

    internal List<Func<object, ValidationError?>> Rules { get; } = new();
    internal List<Func<object, Task<ValidationError?>>> AsyncRules { get; } = new();

    /// <summary>
    /// Creates a new rule set with the specified name.
    /// </summary>
    /// <param name="name">The unique name for this rule set.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="name"/> is null.</exception>
    public RuleSet(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
    }

    /// <summary>
    /// Well-known rule set name that represents the default group.
    /// Rules defined outside of any explicit <c>RuleSet</c> block belong to this group.
    /// When <see cref="DependencyInjection.AbstractValidator{T}.Validate(T)"/> is called without
    /// specifying rule sets, all groups (including default) are executed.
    /// </summary>
    public const string Default = "default";

    /// <summary>
    /// Well-known rule set name for creation operations.
    /// </summary>
    public const string Create = "create";

    /// <summary>
    /// Well-known rule set name for update operations.
    /// </summary>
    public const string Update = "update";

    /// <summary>
    /// Well-known rule set name for deletion operations.
    /// </summary>
    public const string Delete = "delete";
}
