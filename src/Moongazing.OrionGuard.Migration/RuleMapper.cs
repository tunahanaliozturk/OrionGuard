using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Moongazing.OrionGuard.Migration;

/// <summary>
/// The single source of truth for the FluentValidation to OrionGuard rule mapping. Given a rule
/// method name and the argument syntax of one call in a <c>RuleFor(...)</c> chain, it decides
/// whether the rule has a safe OrionGuard compatibility-layer equivalent or must be reported.
/// </summary>
/// <remarks>
/// The target surface is <c>Moongazing.OrionGuard.Compatibility.FluentRuleBuilder&lt;T, TProperty&gt;</c>
/// (reached through <c>FluentStyleValidator&lt;T&gt;.RuleFor</c>). Only methods that provably exist on
/// that type are emitted; everything else is reported rather than guessed at, so the codemod never
/// produces code that fails to compile or silently changes behaviour.
/// </remarks>
public static class RuleMapper
{
    /// <summary>
    /// Maps one rule invocation. The decision is intentionally conservative: a rule is only treated
    /// as supported when both its name and its argument shape are known to translate safely.
    /// </summary>
    /// <param name="methodName">The FluentValidation method name, for example <c>MaximumLength</c>.</param>
    /// <param name="arguments">The argument list of the call, used to reject unsupported overloads.</param>
    /// <returns>A <see cref="RuleMapping"/> describing the outcome.</returns>
    public static RuleMapping Map(string methodName, ArgumentListSyntax arguments)
    {
        ArgumentNullException.ThrowIfNull(methodName);
        ArgumentNullException.ThrowIfNull(arguments);

        var argCount = arguments.Arguments.Count;

        return methodName switch
        {
            // Presence / emptiness -- identical names and shapes on the compatibility builder.
            "NotNull" when argCount == 0 => RuleMapping.Supported("NotNull"),
            "NotEmpty" when argCount == 0 => RuleMapping.Supported("NotEmpty"),

            // Equality -- identical.
            "Equal" when argCount == 1 => RuleMapping.Supported("Equal"),
            "NotEqual" when argCount == 1 => RuleMapping.Supported("NotEqual"),

            // String length -- identical names and arities.
            "Length" when argCount == 2 => RuleMapping.Supported("Length"),
            "MinimumLength" when argCount == 1 => RuleMapping.Supported("MinimumLength"),
            "MaximumLength" when argCount == 1 => RuleMapping.Supported("MaximumLength"),

            // FluentValidation's ExactLength(n) is exactly the inclusive length range [n, n].
            "ExactLength" when argCount == 1 =>
                RuleMapping.Supported("Length", ArgumentTransform.DuplicateSingleArgument),

            // String format -- identical.
            "Matches" when argCount == 1 => RuleMapping.Supported("Matches"),
            "EmailAddress" when argCount == 0 => RuleMapping.Supported("EmailAddress"),

            // Numeric comparison -- identical names and arities.
            "GreaterThan" when argCount == 1 => RuleMapping.Supported("GreaterThan"),
            "GreaterThanOrEqualTo" when argCount == 1 => RuleMapping.Supported("GreaterThanOrEqualTo"),
            "LessThan" when argCount == 1 => RuleMapping.Supported("LessThan"),
            "LessThanOrEqualTo" when argCount == 1 => RuleMapping.Supported("LessThanOrEqualTo"),
            "InclusiveBetween" when argCount == 2 => RuleMapping.Supported("InclusiveBetween"),
            "ExclusiveBetween" when argCount == 2 => RuleMapping.Supported("ExclusiveBetween"),

            // Custom predicate -- only the single-argument predicate form has an equivalent.
            "Must" when argCount == 1 => RuleMapping.Supported("Must"),

            // Message / code / condition modifiers -- single-argument forms map across.
            "WithMessage" when argCount == 1 => RuleMapping.Supported("WithMessage"),
            "WithErrorCode" when argCount == 1 => RuleMapping.Supported("WithErrorCode"),
            "When" when argCount == 1 => RuleMapping.Supported("When"),
            "Unless" when argCount == 1 => RuleMapping.Supported("Unless"),

            // Known FluentValidation rules with no safe compatibility equivalent. These are named
            // explicitly so the report carries a precise reason instead of a generic "unknown rule".
            "Null" => RuleMapping.Unsupported(
                "FluentValidation Null() has no OrionGuard compatibility equivalent"),
            "Empty" => RuleMapping.Unsupported(
                "FluentValidation Empty() has no OrionGuard compatibility equivalent"),
            "WithName" or "OverridePropertyName" => RuleMapping.Unsupported(
                "property-name overrides are not supported by the compatibility builder"),
            "Cascade" => RuleMapping.Unsupported(
                "cascade mode has no OrionGuard compatibility equivalent"),
            "ScalePrecision" or "PrecisionScale" => RuleMapping.Unsupported(
                "decimal scale/precision validation has no OrionGuard compatibility equivalent"),
            "MustAsync" => RuleMapping.Unsupported(
                "async predicates are not supported on the compatibility builder"),
            "SetValidator" => RuleMapping.Unsupported(
                "child-object validators (SetValidator) are not auto-migrated"),
            "Custom" => RuleMapping.Unsupported(
                "Custom() rules are not auto-migrated"),
            "InjectValidator" => RuleMapping.Unsupported(
                "InjectValidator() is not auto-migrated"),
            "ChildRules" => RuleMapping.Unsupported(
                "inline ChildRules() are not auto-migrated"),
            "DependentRules" => RuleMapping.Unsupported(
                "DependentRules() blocks are not auto-migrated"),

            // Overloads we recognise by name but whose argument shape we do not translate.
            "Must" => RuleMapping.Unsupported(
                "only the single-predicate Must(...) overload is auto-migrated"),
            "WithMessage" => RuleMapping.Unsupported(
                "only the constant-string WithMessage(...) overload is auto-migrated"),
            "EmailAddress" => RuleMapping.Unsupported(
                "EmailAddress(mode) with an explicit mode is not auto-migrated"),
            "When" or "Unless" => RuleMapping.Unsupported(
                "only the single-predicate When/Unless(...) overload is auto-migrated"),

            // Anything else is an unknown rule (very likely a custom extension method).
            _ => RuleMapping.Unsupported(
                $"unrecognised rule '{methodName}' (custom or unsupported)"),
        };
    }
}
