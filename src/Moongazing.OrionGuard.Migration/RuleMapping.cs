namespace Moongazing.OrionGuard.Migration;

/// <summary>
/// The result of asking <see cref="RuleMapper"/> to map a single FluentValidation rule call.
/// Either the rule maps onto an OrionGuard compatibility method (<see cref="IsSupported"/> true,
/// carrying the target method name and how its arguments are transformed), or it is unsupported
/// and must be reported and left untouched.
/// </summary>
public sealed class RuleMapping
{
    private RuleMapping(
        bool isSupported,
        string? targetMethod,
        ArgumentTransform argumentTransform,
        string? unsupportedReason)
    {
        IsSupported = isSupported;
        TargetMethod = targetMethod;
        ArgumentTransform = argumentTransform;
        UnsupportedReason = unsupportedReason;
    }

    /// <summary>True when the rule has a safe OrionGuard equivalent.</summary>
    public bool IsSupported { get; }

    /// <summary>
    /// The OrionGuard compatibility method name to emit when <see cref="IsSupported"/> is true.
    /// </summary>
    public string? TargetMethod { get; }

    /// <summary>How the original argument list is transformed onto the target method.</summary>
    public ArgumentTransform ArgumentTransform { get; }

    /// <summary>
    /// A human-readable reason the rule was not migrated, when <see cref="IsSupported"/> is false.
    /// </summary>
    public string? UnsupportedReason { get; }

    /// <summary>Creates a supported mapping that keeps the argument list verbatim.</summary>
    public static RuleMapping Supported(string targetMethod) =>
        new(true, targetMethod, ArgumentTransform.Verbatim, null);

    /// <summary>Creates a supported mapping that applies an argument transform.</summary>
    public static RuleMapping Supported(string targetMethod, ArgumentTransform transform) =>
        new(true, targetMethod, transform, null);

    /// <summary>Creates an unsupported mapping carrying the reason it was not migrated.</summary>
    public static RuleMapping Unsupported(string reason) =>
        new(false, null, ArgumentTransform.Verbatim, reason);
}

/// <summary>
/// How a rule's original argument list is rewritten onto the OrionGuard target method.
/// </summary>
public enum ArgumentTransform
{
    /// <summary>Emit the original arguments unchanged.</summary>
    Verbatim,

    /// <summary>
    /// Duplicate a single argument into two (used to express FluentValidation's
    /// <c>ExactLength(n)</c> as the compatibility <c>Length(n, n)</c>).
    /// </summary>
    DuplicateSingleArgument,
}
