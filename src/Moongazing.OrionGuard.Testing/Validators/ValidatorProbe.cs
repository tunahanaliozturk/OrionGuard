using System.Globalization;
using System.Reflection;
using Moongazing.OrionGuard.Attributes;
using Moongazing.OrionGuard.Core;
using Moongazing.OrionGuard.DependencyInjection;

namespace Moongazing.OrionGuard.Testing.Validators;

/// <summary>What the validator reported for one probe value of one property.</summary>
internal sealed record ProbeOutcome(string Probe, IReadOnlyList<ValidationError> Errors, string? Thrown);

/// <summary>Everything observed for one property of the model.</summary>
internal sealed record PropertyProbeReport(
    string Property,
    bool HasValidationAttribute,
    IReadOnlyList<ProbeOutcome> Outcomes)
{
    /// <summary>
    /// True when a rule for this property was found. Either statically (a
    /// <see cref="ValidationAttribute"/> on the property) or behaviourally (the validator reported
    /// at least one error naming it). A thrown exception does not count: it is a defect, not a rule.
    /// </summary>
    public bool IsValidated =>
        HasValidationAttribute || Outcomes.Any(outcome => outcome.Errors.Count > 0);
}

/// <summary>The full sweep of a validator over one model type.</summary>
internal sealed record ValidatorProbeReport(
    string ValidatorName,
    string ModelName,
    IReadOnlyList<PropertyProbeReport> Properties,
    IReadOnlyList<ValidationError> Unattributed)
{
    /// <summary>
    /// True when at least one rule was found anywhere. When this is false the validator is
    /// indistinguishable from one with no rules at all, so callers must fail loudly instead of
    /// reporting the model as fully unvalidated.
    /// </summary>
    public bool SawAnyRule =>
        Unattributed.Count > 0 || Properties.Any(property => property.IsValidated);
}

/// <summary>
/// Runs a validator over a fixed, ordered set of probe values and records what it reported.
/// </summary>
/// <remarks>
/// A validator's rules are closures over an opaque predicate -- neither <c>AbstractValidator&lt;T&gt;</c>
/// nor <c>FluentStyleValidator&lt;T&gt;</c> exposes the property a rule targets -- so rules are
/// discovered by running the validator and reading <see cref="ValidationError.ParameterName"/>
/// back out. Everything here is fixed data: probe values are literals, ordering is ordinal, and
/// every number and date is formatted with <see cref="CultureInfo.InvariantCulture"/>, so two runs
/// on two machines produce the same report.
/// </remarks>
internal static class ValidatorProbe
{
    private static readonly string LongString = new('x', 256);
    private static readonly Guid NonEmptyGuid = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTime PastUtc = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime FutureUtc = new(2100, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>A single probe value together with the literal that represents it in a report.</summary>
    private readonly record struct ProbeValue(object? Value, string Label);

    public static ValidatorProbeReport Run<TValidator, TModel>()
        where TValidator : IValidator<TModel>, new()
        where TModel : class, new()
    {
        var validator = new TValidator();

        var properties = typeof(TModel)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .ToArray();

        var known = new HashSet<string>(properties.Select(property => property.Name), StringComparer.Ordinal);
        var unattributed = new List<ValidationError>();
        var seenUnattributed = new HashSet<string>(StringComparer.Ordinal);
        var reports = new List<PropertyProbeReport>(properties.Length);

        // A get-only property cannot be assigned a probe value, so the default instance is the
        // only observation available for it.
        var baseline = Observe(validator, static () => new TModel());
        Collect(baseline.Errors, known, unattributed, seenUnattributed);

        foreach (var property in properties)
        {
            var outcomes = new List<ProbeOutcome>();

            if (!property.CanWrite)
            {
                outcomes.Add(new ProbeOutcome("(default)", Attributed(baseline.Errors, property.Name), baseline.Thrown));
            }
            else
            {
                foreach (var probe in ProbesFor(property.PropertyType))
                {
                    var run = Observe(validator, () =>
                    {
                        var model = new TModel();
                        property.SetValue(model, probe.Value);
                        return model;
                    });

                    outcomes.Add(new ProbeOutcome(probe.Label, Attributed(run.Errors, property.Name), run.Thrown));
                    Collect(run.Errors, known, unattributed, seenUnattributed);
                }
            }

            var hasAttribute = property.GetCustomAttributes<ValidationAttribute>(inherit: true).Any();
            reports.Add(new PropertyProbeReport(property.Name, hasAttribute, outcomes));
        }

        unattributed.Sort(static (left, right) =>
        {
            var byParameter = string.CompareOrdinal(left.ParameterName, right.ParameterName);
            if (byParameter != 0) return byParameter;
            var byCode = string.CompareOrdinal(left.ErrorCode, right.ErrorCode);
            return byCode != 0 ? byCode : string.CompareOrdinal(left.Message, right.Message);
        });

        return new ValidatorProbeReport(typeof(TValidator).Name, typeof(TModel).Name, reports, unattributed);
    }

    private static (IReadOnlyList<ValidationError> Errors, string? Thrown) Observe<TModel>(
        IValidator<TModel> validator, Func<TModel> build) where TModel : class
    {
        try
        {
            return (validator.Validate(build()).AllIssues, null);
        }
        catch (Exception exception)
        {
            // A validator that dereferences a null probe -- or a setter that guards its input --
            // throws instead of reporting. Swallowing it here keeps the remaining probes running,
            // and only the exception TYPE is recorded because messages can carry machine-specific
            // paths and line numbers.
            return (Array.Empty<ValidationError>(), exception.GetType().Name);
        }
    }

    /// <summary>
    /// Errors naming <paramref name="propertyName"/>. A cross-property rule reports both names in
    /// one comma-separated <see cref="ValidationError.ParameterName"/>, so it counts for each.
    /// </summary>
    private static IReadOnlyList<ValidationError> Attributed(
        IReadOnlyList<ValidationError> errors, string propertyName)
        => errors.Where(error => Names(error).Contains(propertyName, StringComparer.Ordinal)).ToArray();

    /// <summary>Records errors whose parameter name maps to no property of the model.</summary>
    private static void Collect(
        IReadOnlyList<ValidationError> errors,
        HashSet<string> known,
        List<ValidationError> unattributed,
        HashSet<string> seen)
    {
        foreach (var error in errors)
        {
            if (Names(error).Any(known.Contains)) continue;
            if (seen.Add($"{error.ParameterName}{error.ErrorCode}{error.Message}"))
            {
                unattributed.Add(error);
            }
        }
    }

    private static IEnumerable<string> Names(ValidationError error)
        => error.ParameterName.Split(',').Select(name => name.Trim());

    private static List<ProbeValue> ProbesFor(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type);
        var core = underlying ?? type;
        var probes = new List<ProbeValue>();

        // null goes first: it is the probe that finds a missing NotNull rule.
        if (!type.IsValueType || underlying is not null)
        {
            probes.Add(new ProbeValue(null, "null"));
        }

        if (core == typeof(string))
        {
            probes.Add(new ProbeValue(string.Empty, "\"\""));
            probes.Add(new ProbeValue("   ", "\"   \""));
            probes.Add(new ProbeValue("x", "\"x\""));
            probes.Add(new ProbeValue(LongString, "string(256)"));
            return probes;
        }

        if (core == typeof(bool))
        {
            probes.Add(new ProbeValue(false, "false"));
            probes.Add(new ProbeValue(true, "true"));
            return probes;
        }

        if (core.IsEnum)
        {
            foreach (var value in Enum.GetValues(core))
            {
                probes.Add(new ProbeValue(value, Enum.GetName(core, value) ?? value.ToString()!));
            }
            return probes;
        }

        if (core == typeof(Guid))
        {
            probes.Add(new ProbeValue(Guid.Empty, Guid.Empty.ToString()));
            probes.Add(new ProbeValue(NonEmptyGuid, NonEmptyGuid.ToString()));
            return probes;
        }

        if (core == typeof(DateTime))
        {
            probes.Add(new ProbeValue(DateTime.MinValue, Format(DateTime.MinValue)));
            probes.Add(new ProbeValue(PastUtc, Format(PastUtc)));
            probes.Add(new ProbeValue(FutureUtc, Format(FutureUtc)));
            return probes;
        }

        if (core == typeof(DateTimeOffset))
        {
            var past = new DateTimeOffset(PastUtc);
            var future = new DateTimeOffset(FutureUtc);
            probes.Add(new ProbeValue(DateTimeOffset.MinValue, Format(DateTimeOffset.MinValue.UtcDateTime)));
            probes.Add(new ProbeValue(past, Format(past.UtcDateTime)));
            probes.Add(new ProbeValue(future, Format(future.UtcDateTime)));
            return probes;
        }

        if (TryNumericProbes(core, probes)) return probes;
        if (TryCollectionProbes(core, probes)) return probes;

        if (core.IsValueType)
        {
            probes.Add(new ProbeValue(Activator.CreateInstance(core), "default"));
        }

        return probes;
    }

    private static string Format(DateTime value)
        => value.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static bool TryNumericProbes(Type core, List<ProbeValue> probes)
    {
        // Unsigned types cannot hold -1, so they get 0/1/2 where the signed types get -1/0/1.
        int[]? raw = Type.GetTypeCode(core) switch
        {
            TypeCode.SByte or TypeCode.Int16 or TypeCode.Int32 or TypeCode.Int64 or
            TypeCode.Single or TypeCode.Double or TypeCode.Decimal => [-1, 0, 1],
            TypeCode.Byte or TypeCode.UInt16 or TypeCode.UInt32 or TypeCode.UInt64 => [0, 1, 2],
            _ => null,
        };

        if (raw is null) return false;

        foreach (var number in raw)
        {
            var value = Convert.ChangeType(number, core, CultureInfo.InvariantCulture);
            probes.Add(new ProbeValue(value, ((IFormattable)value).ToString(null, CultureInfo.InvariantCulture)));
        }

        return true;
    }

    private static bool TryCollectionProbes(Type core, List<ProbeValue> probes)
    {
        if (!typeof(System.Collections.IEnumerable).IsAssignableFrom(core)) return false;

        var elementType = core.IsArray
            ? core.GetElementType()!
            : core.GetInterfaces()
                  .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                  ?.GetGenericArguments()[0] ?? typeof(object);

        var emptyArray = Array.CreateInstance(elementType, 0);
        var singleArray = Array.CreateInstance(elementType, 1);

        if (core.IsInstanceOfType(emptyArray))
        {
            probes.Add(new ProbeValue(emptyArray, "[]"));
            probes.Add(new ProbeValue(singleArray, "[default]"));
            return true;
        }

        // Not array-assignable (List<T>, ICollection<T>, ...): build the list instead. Anything
        // neither an array nor a list can hold gets only the null probe.
        var listType = typeof(List<>).MakeGenericType(elementType);
        if (!core.IsAssignableFrom(listType)) return true;

        var single = (System.Collections.IList)Activator.CreateInstance(listType)!;
        single.Add(singleArray.GetValue(0));
        probes.Add(new ProbeValue(Activator.CreateInstance(listType), "[]"));
        probes.Add(new ProbeValue(single, "[default]"));
        return true;
    }
}
