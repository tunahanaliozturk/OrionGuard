using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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

            // String length. FluentValidation's Length(min, max) also has a Func<T, int> overload, which
            // the int-only compatibility method cannot take, so lambda bounds are reported.
            "Length" when argCount == 2 && !IsLambda(args[0]) && !IsLambda(args[1]) =>
                RuleMapping.Supported("Length"),
            "MinimumLength" when argCount == 1 => RuleMapping.Supported("MinimumLength"),
            "MaximumLength" when argCount == 1 => RuleMapping.Supported("MaximumLength"),

            // FluentValidation's exact-length rule is Length(n), which is exactly the inclusive range [n, n].
            "Length" when argCount == 1 && !IsLambda(args[0]) =>
                RuleMapping.Supported("Length", ArgumentTransform.DuplicateSingleArgument),

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

            // Custom predicate. Only Must(value => ...) has an equivalent; the (instance, value) and
            // (instance, value, context) lambdas are single arguments too, but would not compile against
            // the compatibility builder's Func<TProperty, bool>.
            "Must" when argCount == 1 && IsSingleParameterLambda(args[0]) => RuleMapping.Supported("Must"),

            // Message / code / condition modifiers. FluentValidation expands placeholders such as
            // {PropertyName} in a WithMessage text and the compatibility builder prints the text verbatim,
            // so only a string literal without braces is known to produce the same message.
            "WithMessage" when argCount == 1 && IsLiteralWithoutPlaceholders(args[0]) =>
                RuleMapping.Supported("WithMessage"),
            "WithErrorCode" when argCount == 1 && !IsLambda(args[0]) => RuleMapping.Supported("WithErrorCode"),
            "When" when argCount == 1 && IsSingleParameterLambda(args[0]) => RuleMapping.Supported("When"),
            "Unless" when argCount == 1 && IsSingleParameterLambda(args[0]) => RuleMapping.Supported("Unless"),

            // TODO: map Matches once the compatibility builder matches FluentValidation, which fails an
            // empty or whitespace string the pattern does not match (the builder skips blank values), and
            // add the Regex and Func<T, string> overloads the builder lacks.
            "Matches" => RuleMapping.Unsupported(
                "Matches() differs from FluentValidation: the compatibility builder skips empty and whitespace " +
                "values, FluentValidation matches them against the pattern"),

            // TODO: map EmailAddress once the compatibility builder offers FluentValidation's check (one '@',
            // neither first nor last character). The builder uses a stricter regex and skips blank values.
            "EmailAddress" when argCount == 0 => RuleMapping.Unsupported(
                "EmailAddress() differs from FluentValidation: FluentValidation only requires a single '@' " +
                "that is neither first nor last and rejects an empty string, the compatibility builder uses a " +
                "stricter pattern and skips empty values"),

            // ExactLength is not a FluentValidation rule (its exact-length rule is Length(n)); an extension
            // method of that name is the project's own, and its semantics are unknown.
            "ExactLength" => RuleMapping.Unsupported(
                "ExactLength() is not a FluentValidation built-in (FluentValidation's exact-length rule is " +
                "Length(n)); custom rules are not auto-migrated"),

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
                "only the value-predicate Must(value => ...) overload is auto-migrated"),
            "WithMessage" => RuleMapping.Unsupported(
                "only WithMessage(\"...\") with a string literal and no {placeholders} is auto-migrated; " +
                "FluentValidation formats placeholders the compatibility builder prints verbatim, and the " +
                "message-factory (lambda) overload has no equivalent"),
            "WithErrorCode" => RuleMapping.Unsupported(
                "only the constant-string WithErrorCode(code) overload is auto-migrated"),
            "EmailAddress" => RuleMapping.Unsupported(
                "EmailAddress(mode) with an explicit mode is not auto-migrated"),
            "Length" => RuleMapping.Unsupported(
                "only Length(min, max) and Length(n) with constant bounds are auto-migrated; the Func<T, int> " +
                "overloads are not"),
            "When" or "Unless" => RuleMapping.Unsupported(
                $"only the single-predicate {methodName}(x => ...) overload is auto-migrated"),

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

    /// <summary>
    /// True for <c>x =&gt; ...</c> or <c>(x) =&gt; ...</c>. FluentValidation's <c>Must</c>, <c>When</c> and
    /// <c>Unless</c> also take two- and three-parameter lambdas (instance, value, context) in a single argument;
    /// the compatibility builder only has the one-parameter delegates.
    /// </summary>
    private static bool IsSingleParameterLambda(ArgumentSyntax argument) => argument.Expression switch
    {
        SimpleLambdaExpressionSyntax => true,
        ParenthesizedLambdaExpressionSyntax parenthesized => parenthesized.ParameterList.Parameters.Count == 1,
        _ => false,
    };

    /// <summary>
    /// True for a string literal (regular, verbatim or raw) that contains no <c>{</c>. Any other expression
    /// (a constant, a resource lookup, an interpolated string) may hold placeholders at run time, and a
    /// literal with a brace may be one.
    /// </summary>
    private static bool IsLiteralWithoutPlaceholders(ArgumentSyntax argument) =>
        argument.Expression is LiteralExpressionSyntax literal &&
        literal.IsKind(SyntaxKind.StringLiteralExpression) &&
        !literal.Token.ValueText.Contains('{', StringComparison.Ordinal);
}
