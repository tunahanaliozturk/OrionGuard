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

        var args = arguments.Arguments;
        var argCount = args.Count;

        return methodName switch
        {
            // Presence / emptiness -- identical names and shapes on the compatibility builder.
            "NotNull" when argCount == 0 => RuleMapping.Supported("NotNull"),
            "NotEmpty" when argCount == 0 => RuleMapping.Supported("NotEmpty"),

            // Equality. The compatibility builder takes a constant comparison value, so the
            // member-reference overload Equal(x => x.Other) -- also single-argument but a lambda --
            // must NOT be mapped onto it; it is reported instead of mistranslated.
            "Equal" when argCount == 1 && !IsLambda(args[0]) => RuleMapping.Supported("Equal"),
            "NotEqual" when argCount == 1 && !IsLambda(args[0]) => RuleMapping.Supported("NotEqual"),

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

            // Numeric comparison. The compatibility builder compares against a constant IComparable
            // threshold, so the member-reference overload GreaterThan(x => x.Other) -- single-argument
            // but a lambda -- is reported rather than mapped onto the constant-threshold method.
            "GreaterThan" when argCount == 1 && !IsLambda(args[0]) => RuleMapping.Supported("GreaterThan"),
            "GreaterThanOrEqualTo" when argCount == 1 && !IsLambda(args[0]) =>
                RuleMapping.Supported("GreaterThanOrEqualTo"),
            "LessThan" when argCount == 1 && !IsLambda(args[0]) => RuleMapping.Supported("LessThan"),
            "LessThanOrEqualTo" when argCount == 1 && !IsLambda(args[0]) =>
                RuleMapping.Supported("LessThanOrEqualTo"),
            "InclusiveBetween" when argCount == 2 => RuleMapping.Supported("InclusiveBetween"),
            "ExclusiveBetween" when argCount == 2 => RuleMapping.Supported("ExclusiveBetween"),

            // Custom predicate -- only the single-argument predicate (lambda) form has an equivalent.
            "Must" when argCount == 1 && IsLambda(args[0]) => RuleMapping.Supported("Must"),

            // Message / code / condition modifiers -- single-argument forms map across. WithMessage
            // only translates the constant-string overload; the message-factory lambda overload
            // WithMessage(x => ...) is reported, not mapped.
            "WithMessage" when argCount == 1 && !IsLambda(args[0]) => RuleMapping.Supported("WithMessage"),
            "WithErrorCode" when argCount == 1 && !IsLambda(args[0]) => RuleMapping.Supported("WithErrorCode"),
            "When" when argCount == 1 && IsLambda(args[0]) => RuleMapping.Supported("When"),
            "Unless" when argCount == 1 && IsLambda(args[0]) => RuleMapping.Supported("Unless"),

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

            // Overloads we recognise by name but whose argument shape we do not translate. These
            // are reported (and the chain left untouched) rather than mistranslated onto a method
            // with different semantics.
            "Equal" or "NotEqual" => RuleMapping.Unsupported(
                $"only the constant-value {methodName}(value) overload is auto-migrated; the " +
                "member-comparison (lambda) overload is not"),
            "GreaterThan" or "GreaterThanOrEqualTo" or "LessThan" or "LessThanOrEqualTo" =>
                RuleMapping.Unsupported(
                    $"only the constant-threshold {methodName}(value) overload is auto-migrated; the " +
                    "member-comparison (lambda) overload is not"),
            "Must" => RuleMapping.Unsupported(
                "only the single-predicate Must(predicate) overload is auto-migrated"),
            "WithMessage" => RuleMapping.Unsupported(
                "only the constant-string WithMessage(message) overload is auto-migrated; the " +
                "message-factory (lambda) overload is not"),
            "WithErrorCode" => RuleMapping.Unsupported(
                "only the constant-string WithErrorCode(code) overload is auto-migrated"),
            "EmailAddress" => RuleMapping.Unsupported(
                "EmailAddress(mode) with an explicit mode is not auto-migrated"),
            "When" or "Unless" => RuleMapping.Unsupported(
                $"only the single-predicate {methodName}(predicate) overload is auto-migrated"),

            // Anything else is an unknown rule (very likely a custom extension method).
            _ => RuleMapping.Unsupported(
                $"unrecognised rule '{methodName}' (custom or unsupported)"),
        };
    }

    /// <summary>
    /// True when the argument is a lambda expression. Several FluentValidation rules expose a
    /// member-reference / factory overload that takes a lambda but has the same arity as the
    /// constant-value overload the compatibility builder supports (for example
    /// <c>GreaterThan(x =&gt; x.Other)</c> versus <c>GreaterThan(10)</c>). Distinguishing the two
    /// by argument shape is what keeps the mapper from mistranslating an unsupported overload.
    /// </summary>
    private static bool IsLambda(ArgumentSyntax argument) =>
        argument.Expression is LambdaExpressionSyntax;
}
