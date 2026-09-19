using Moongazing.OrionGuard.DependencyInjection;

namespace Moongazing.OrionGuard.Testing.Validators;

/// <summary>
/// Entry point for rule coverage: reports which properties of a model carry no rule at all, so a
/// test fails when someone adds a property and forgets to validate it.
/// </summary>
/// <example>
/// <code>
/// [Fact]
/// public void Every_CreateUserRequest_property_is_validated()
///     =&gt; OrionGuardCoverage.For&lt;CreateUserValidator, CreateUserRequest&gt;()
///         .AssertEveryPropertyIsValidated(except: [nameof(CreateUserRequest.Notes)]);
/// </code>
/// </example>
public static class OrionGuardCoverage
{
    /// <summary>
    /// Measures which properties of <typeparamref name="TModel"/> are covered by a rule in
    /// <typeparamref name="TValidator"/>.
    /// </summary>
    /// <typeparam name="TValidator">The validator under test. Needs a public parameterless constructor.</typeparam>
    /// <typeparam name="TModel">The validated model. Needs a public parameterless constructor.</typeparam>
    /// <exception cref="ValidatorAssertionException">
    /// <typeparamref name="TValidator"/>'s rules could not be enumerated at all; see
    /// <see cref="ValidatorCoverage{TValidator, TModel}"/> for what is and is not discoverable.
    /// </exception>
    public static ValidatorCoverage<TValidator, TModel> For<TValidator, TModel>()
        where TValidator : IValidator<TModel>, new()
        where TModel : class, new()
        => new();
}

/// <summary>
/// Rule coverage for one validator over one model. Created by
/// <see cref="OrionGuardCoverage.For{TValidator, TModel}"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>What this can see.</b> A rule is found either statically -- a
/// <c>Moongazing.OrionGuard.Attributes.ValidationAttribute</c> on the property -- or behaviourally,
/// by running the validator over a fixed set of probe values per property and reading the
/// <c>ParameterName</c> off every error it reports. Behavioural discovery is the only option for
/// <c>AbstractValidator&lt;T&gt;</c> and <c>FluentStyleValidator&lt;T&gt;</c>: both keep their rules
/// as closures over an opaque predicate and expose neither the rule list nor the property a rule
/// targets.
/// </para>
/// <para>
/// <b>What this cannot see.</b> A rule that no probe value can make fail (for example
/// <c>Must(x =&gt; x.Age != 42)</c>) is invisible, and its property is reported as unvalidated. A
/// rule that reports a parameter name which is not a property of the model is attributed to that
/// name, not to a property. A rule that throws instead of reporting -- a predicate dereferencing a
/// null probe -- does not count as coverage. A property carrying a validation attribute counts as
/// validated even when <typeparamref name="TValidator"/> never runs the attribute validator.
/// </para>
/// </remarks>
/// <typeparam name="TValidator">The validator under test.</typeparam>
/// <typeparam name="TModel">The validated model.</typeparam>
public sealed class ValidatorCoverage<TValidator, TModel>
    where TValidator : IValidator<TModel>, new()
    where TModel : class, new()
{
    private readonly HashSet<string> allProperties;

    internal ValidatorCoverage()
    {
        var report = ValidatorProbe.Run<TValidator, TModel>();

        if (!report.SawAnyRule)
        {
            // Reporting every property as unvalidated here would be a guess. A validator with no
            // rules and one whose rules no probe value can trigger look exactly the same from the
            // outside, so say so instead of producing a number nobody can trust.
            throw new ValidatorAssertionException(
                $"{typeof(TValidator).Name} reported nothing for any probe value of {typeof(TModel).Name}, " +
                $"so its rules could not be enumerated. Coverage is derived from what a validator reports at " +
                $"run time: a validator with no rules and one whose rules never fail for a probe value are " +
                $"indistinguishable. Check that the validator has rules and that at least one of them fails " +
                $"for a null, empty, zero, or negative value.");
        }

        allProperties = new HashSet<string>(
            report.Properties.Select(property => property.Property), StringComparer.Ordinal);

        UnvalidatedProperties = report.Properties
            .Where(property => !property.IsValidated)
            .Select(property => property.Property)
            .ToArray();
    }

    /// <summary>
    /// The properties of <typeparamref name="TModel"/> that no rule was found for, in ordinal
    /// order.
    /// </summary>
    public IReadOnlyList<string> UnvalidatedProperties { get; }

    /// <summary>
    /// Throws unless every property of <typeparamref name="TModel"/> carries at least one rule.
    /// </summary>
    /// <param name="except">
    /// Properties that are deliberately unvalidated, by name. Each entry must be a real property of
    /// <typeparamref name="TModel"/>, so a renamed property does not leave a stale exemption behind.
    /// </param>
    /// <exception cref="ValidatorAssertionException">
    /// A property outside <paramref name="except"/> carries no rule, or <paramref name="except"/>
    /// names something that is not a property of <typeparamref name="TModel"/>.
    /// </exception>
    public void AssertEveryPropertyIsValidated(params string[] except)
    {
        var exempt = except ?? [];

        var unknown = exempt.Where(name => !allProperties.Contains(name)).ToArray();
        if (unknown.Length > 0)
        {
            throw new ValidatorAssertionException(
                $"'except' names {string.Join(", ", unknown)}, which {(unknown.Length == 1 ? "is not a property" : "are not properties")} " +
                $"of {typeof(TModel).Name}. Remove the entry or fix the name.");
        }

        var missing = UnvalidatedProperties.Where(name => !exempt.Contains(name, StringComparer.Ordinal)).ToArray();
        if (missing.Length == 0)
        {
            return;
        }

        throw new ValidatorAssertionException(
            $"{typeof(TValidator).Name} has no rule for {missing.Length} " +
            $"{(missing.Length == 1 ? "property" : "properties")} of {typeof(TModel).Name}: " +
            $"{string.Join(", ", missing)}. Add a rule, or pass the name in 'except' when the property is " +
            $"deliberately unvalidated.");
    }
}
