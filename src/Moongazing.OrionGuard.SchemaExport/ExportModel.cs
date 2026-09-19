using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Moongazing.OrionGuard.Attributes;

namespace Moongazing.OrionGuard.SchemaExport;

/// <summary>
/// The trim/AOT warning text shared by every entry point, so a consumer reads the same
/// explanation wherever the analyzer points them.
/// </summary>
internal static class SchemaExportAnnotations
{
    internal const string UnreferencedCode =
        "The schema exporters walk the exported type's public properties, and the types of those " +
        "properties, with reflection. Root the exported types (for example with DynamicDependency) " +
        "or their members may be trimmed away and the artifact will be missing them.";
}

/// <summary>
/// One exported member: its serialized name, its shape, and the constraints read from the
/// OrionGuard validation attributes declared on it.
/// </summary>
internal sealed class MemberModel
{
    internal required string Name { get; init; }
    internal required Type ClrType { get; init; }
    internal required bool IsRequired { get; init; }
    internal required bool IsNullable { get; init; }
    internal required IReadOnlyList<string> Unsupported { get; init; }
    internal int? MinLength { get; init; }
    internal int? MaxLength { get; init; }
    internal string? Pattern { get; init; }
    internal double? Minimum { get; init; }
    internal double? Maximum { get; init; }
    internal double? ExclusiveMinimum { get; init; }
    internal string? Format { get; init; }

    internal bool HasConstraints =>
        IsRequired || MinLength is not null || MaxLength is not null || Pattern is not null
        || Minimum is not null || Maximum is not null || ExclusiveMinimum is not null || Format is not null;
}

/// <summary>
/// The exported type together with every object type reachable from it, read once so the JSON
/// Schema and the TypeScript exporters describe exactly the same members in the same order.
/// </summary>
internal sealed class ExportModel
{
    private readonly Dictionary<Type, IReadOnlyList<MemberModel>> members;

    private ExportModel(
        Type root,
        IReadOnlyList<Type> types,
        IReadOnlyDictionary<Type, string> names,
        Dictionary<Type, IReadOnlyList<MemberModel>> members)
    {
        Root = root;
        Types = types;
        Names = names;
        this.members = members;
    }

    /// <summary>The type the export was requested for. It is always the first entry in <see cref="Types"/>.</summary>
    internal Type Root { get; }

    /// <summary>The root type followed by every nested object type, in the order they were discovered.</summary>
    internal IReadOnlyList<Type> Types { get; }

    /// <summary>The name each type is published under. Unique within one export.</summary>
    internal IReadOnlyDictionary<Type, string> Names { get; }

    internal IReadOnlyList<MemberModel> MembersOf(Type type) => members[type];

    [RequiresUnreferencedCode(SchemaExportAnnotations.UnreferencedCode)]
    internal static ExportModel Build(Type root)
    {
        // NullabilityInfoContext is documented as not thread-safe, so it is created per export
        // rather than cached in a static.
        var nullability = new NullabilityInfoContext();
        var names = new Dictionary<Type, string>();
        var taken = new HashSet<string>(StringComparer.Ordinal);
        var members = new Dictionary<Type, IReadOnlyList<MemberModel>>();
        var order = new List<Type>();
        var queue = new Queue<Type>();

        Discover(root);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var read = Read(current, nullability);
            members[current] = read;

            foreach (var member in read)
            {
                var target = TypeShapes.GetElementType(member.ClrType) ?? member.ClrType;
                if (TypeShapes.IsExportedObject(target))
                {
                    Discover(target);
                }
            }
        }

        return new ExportModel(root, order, names, members);

        void Discover(Type type)
        {
            if (names.ContainsKey(type))
            {
                return;
            }

            names[type] = UniqueName(type, taken);
            order.Add(type);
            queue.Enqueue(type);
        }
    }

    /// <summary>
    /// Two types with the same short name would otherwise share one definition and silently
    /// describe the wrong shape, so the loser falls back to its namespace-qualified name.
    /// </summary>
    private static string UniqueName(Type type, HashSet<string> taken)
    {
        var name = Sanitize(type.Name);
        if (!taken.Add(name))
        {
            name = Sanitize(type.FullName ?? type.Name);
            taken.Add(name);
        }
        return name;
    }

    /// <summary>
    /// Arity markers, namespaces, and nested-type separators are not valid identifier characters
    /// in TypeScript and read badly as a JSON Schema definition key.
    /// </summary>
    private static string Sanitize(string name) =>
        string.Create(name.Length, name, static (buffer, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                buffer[i] = char.IsLetterOrDigit(source[i]) || source[i] == '_' ? source[i] : '_';
            }
        });

    [RequiresUnreferencedCode(SchemaExportAnnotations.UnreferencedCode)]
    private static IReadOnlyList<MemberModel> Read(Type type, NullabilityInfoContext nullability)
    {
        var models = new List<MemberModel>();

        // Same discovery surface as OrionGuardSchemaFilter and AttributeValidator: public instance
        // properties, and only the ValidationAttributes declared on them.
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetMethod is null
                || property.GetIndexParameters().Length > 0
                || property.GetCustomAttribute<JsonIgnoreAttribute>() is not null)
            {
                continue;
            }

            models.Add(Read(property, nullability));
        }

        return models;
    }

    [RequiresUnreferencedCode(SchemaExportAnnotations.UnreferencedCode)]
    private static MemberModel Read(PropertyInfo property, NullabilityInfoContext nullability)
    {
        var required = false;
        int? minLength = null;
        int? maxLength = null;
        double? minimum = null;
        double? maximum = null;
        double? exclusiveMinimum = null;
        string? pattern = null;
        string? format = null;
        var unsupported = new List<string>();

        foreach (var attribute in property.GetCustomAttributes<ValidationAttribute>())
        {
            switch (attribute)
            {
                // Qualified: System.Diagnostics.CodeAnalysis has a NotNullAttribute of its own.
                case Attributes.NotNullAttribute:
                    required = true;
                    break;

                // NotEmpty rejects null as well as the empty value, so it makes the member
                // required on its own.
                case NotEmptyAttribute:
                    required = true;
                    minLength = Math.Max(minLength ?? 0, 1);
                    break;

                // ValidationAttribute allows multiples, so bounds are narrowed rather than
                // overwritten: the result holds whatever every declared rule holds.
                case LengthAttribute length:
                    minLength = Math.Max(minLength ?? 0, length.MinLength);
                    maxLength = Math.Min(maxLength ?? int.MaxValue, length.MaxLength);
                    break;

                case RangeAttribute range:
                    minimum = Math.Max(minimum ?? double.NegativeInfinity, range.Minimum);
                    maximum = Math.Min(maximum ?? double.PositiveInfinity, range.Maximum);
                    break;

                case RegexAttribute regex:
                    pattern = regex.Pattern;
                    break;

                case EmailAttribute:
                    format = "email";
                    break;

                // JSON Schema 2020-12 carries the bound itself in exclusiveMinimum, not a flag.
                case PositiveAttribute:
                    exclusiveMinimum = 0;
                    break;

                // A rule OrionGuard evaluates by running C# -- a custom predicate, a comparison
                // against a second property. The artifact cannot enforce it, so it is reported
                // instead of dropped.
                default:
                    unsupported.Add(attribute.GetType().Name);
                    break;
            }
        }

        var declared = property.PropertyType;
        var clrType = Nullable.GetUnderlyingType(declared) ?? declared;

        // Unknown means the declaring assembly was compiled without nullable reference types, and
        // the member could then hold null: say so rather than claim a guarantee that is not there.
        var nullable = !required
            && nullability.Create(property).ReadState != NullabilityState.NotNull;

        return new MemberModel
        {
            Name = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
                ?? JsonNamingPolicy.CamelCase.ConvertName(property.Name),
            ClrType = clrType,
            IsRequired = required,
            IsNullable = nullable,
            Unsupported = unsupported,
            MinLength = minLength,
            MaxLength = maxLength,
            Pattern = pattern,
            Minimum = minimum,
            Maximum = maximum,
            ExclusiveMinimum = exclusiveMinimum,
            Format = format ?? TypeShapes.TypeFormat(clrType)
        };
    }
}
