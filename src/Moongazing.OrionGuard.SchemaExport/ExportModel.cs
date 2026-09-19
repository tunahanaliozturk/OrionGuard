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
/// The serialized shape of one value: its CLR type, whether it may be null, and -- when it is a
/// collection -- the shape of its elements. Collections nest, so this is a chain rather than a
/// flag: <c>List&lt;List&lt;Address&gt;&gt;</c> is a shape whose element is a shape whose element
/// is <c>Address</c>, and C# annotates nullability separately at every level.
/// </summary>
internal sealed class ShapeModel
{
    internal required Type ClrType { get; init; }
    internal required bool IsNullable { get; init; }

    /// <summary>The element shape when this value is a JSON array, otherwise null.</summary>
    internal ShapeModel? Element { get; init; }
}

/// <summary>
/// One exported member: its serialized name, its shape, and the constraints read from the
/// OrionGuard validation attributes declared on it.
/// </summary>
internal sealed class MemberModel
{
    internal required string Name { get; init; }
    internal required ShapeModel Shape { get; init; }
    internal required bool IsRequired { get; init; }
    internal required IReadOnlyList<string> Unsupported { get; init; }

    /// <summary>
    /// The declared regex patterns that survived the ECMAScript screen. Several are possible
    /// because <c>ValidationAttribute</c> allows multiples, and they all have to hold.
    /// </summary>
    internal required IReadOnlyList<string> Patterns { get; init; }

    /// <summary>
    /// True when the value must hold a non-whitespace character, which is what <c>[NotEmpty]</c>
    /// means for a string. Kept apart from <see cref="Patterns"/> so the artifacts can say
    /// "not blank" instead of printing the expression that encodes it.
    /// </summary>
    internal bool RequiresNonWhitespace { get; init; }

    internal int? MinLength { get; init; }
    internal int? MaxLength { get; init; }
    internal double? Minimum { get; init; }
    internal double? Maximum { get; init; }
    internal double? ExclusiveMinimum { get; init; }
    internal string? Format { get; init; }

    internal bool HasConstraints =>
        IsRequired || RequiresNonWhitespace || Patterns.Count > 0
        || MinLength is not null || MaxLength is not null
        || Minimum is not null || Maximum is not null || ExclusiveMinimum is not null
        || Format is not null;
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
                // Walk the whole collection chain, not just its outer type: the object worth
                // defining sits at the bottom of List<List<Address>>.
                for (var shape = member.Shape; shape is not null; shape = shape.Element)
                {
                    if (TypeShapes.IsExportedObject(shape.ClrType))
                    {
                        Discover(shape.ClrType);
                    }
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
        var declared = property.PropertyType;
        var clrType = Nullable.GetUnderlyingType(declared) ?? declared;

        var required = false;
        var requiresNonWhitespace = false;
        int? minLength = null;
        int? maxLength = null;
        double? minimum = null;
        double? maximum = null;
        double? exclusiveMinimum = null;
        string? format = null;
        var patterns = new List<string>();
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
                // required on its own. For a string it is IsNullOrWhiteSpace, not a length check:
                // minLength alone would accept "   ", and the client would then pass what the
                // server rejects. The extra requirement is expressible as an unanchored regex --
                // "holds a non-whitespace character" -- so it is encoded rather than reported as
                // a gap. For a collection NotEmpty means "has items", which minItems covers.
                case NotEmptyAttribute:
                    required = true;
                    minLength = Math.Max(minLength ?? 0, 1);
                    requiresNonWhitespace = clrType == typeof(string);
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

                // A JSON Schema pattern is an ECMAScript regex. Copying a .NET-only construct
                // across would make the artifact reject valid input or accept invalid input, so
                // an untranslatable pattern is reported as a gap instead of being emitted.
                case RegexAttribute regex:
                    if (EcmaScriptRegex.IsPortable(regex.Pattern))
                    {
                        patterns.Add(regex.Pattern);
                    }
                    else
                    {
                        unsupported.Add(nameof(RegexAttribute));
                    }
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

        return new MemberModel
        {
            Name = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
                ?? JsonNamingPolicy.CamelCase.ConvertName(property.Name),
            Shape = BuildShape(declared, nullability.Create(property), notNull: required),
            IsRequired = required,
            RequiresNonWhitespace = requiresNonWhitespace,
            Patterns = patterns,
            Unsupported = unsupported,
            MinLength = minLength,
            MaxLength = maxLength,
            Minimum = minimum,
            Maximum = maximum,
            ExclusiveMinimum = exclusiveMinimum,
            Format = format ?? TypeShapes.TypeFormat(clrType)
        };
    }

    /// <summary>
    /// Builds the shape chain for a value, carrying the nullability annotation down into every
    /// collection level so <c>List&lt;string?&gt;</c> does not export a non-null element type.
    /// <c>notNull</c> is set when a declared rule already rules null out, which overrides whatever
    /// the type says.
    /// </summary>
    [RequiresUnreferencedCode(SchemaExportAnnotations.UnreferencedCode)]
    private static ShapeModel BuildShape(Type declared, NullabilityInfo? info, bool notNull)
    {
        var underlying = Nullable.GetUnderlyingType(declared);
        var clrType = underlying ?? declared;

        // A non-nullable value type can never hold null whatever the annotations say. For a
        // reference type, Unknown means the declaring assembly was compiled without nullable
        // reference types: say it may be null rather than claim a guarantee that is not there.
        var nullable = !notNull
            && (declared.IsValueType ? underlying is not null : info?.ReadState != NullabilityState.NotNull);

        var elementType = TypeShapes.GetElementType(clrType);

        return new ShapeModel
        {
            ClrType = clrType,
            IsNullable = nullable,
            Element = elementType is null
                ? null
                : BuildShape(elementType, ElementInfo(clrType, elementType, info), notNull: false)
        };
    }

    /// <summary>
    /// The nullability annotation for a collection's elements: the array element for <c>T[]</c>,
    /// the single generic argument for <c>List&lt;T&gt;</c> and its kin. A type that reaches
    /// <see cref="IEnumerable{T}"/> some other way carries no annotation here, and its elements
    /// are then treated as nullable on the same "nothing rules null out" grounds.
    /// </summary>
    private static NullabilityInfo? ElementInfo(Type collection, Type elementType, NullabilityInfo? info)
    {
        if (info is null)
        {
            return null;
        }

        if (collection.IsArray)
        {
            return info.ElementType;
        }

        return collection.IsGenericType
            && collection.GetGenericArguments() is [var argument] && argument == elementType
            && info.GenericTypeArguments is [var annotation]
            ? annotation
            : null;
    }
}
