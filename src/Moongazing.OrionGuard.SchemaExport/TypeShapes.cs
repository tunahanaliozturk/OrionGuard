using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace Moongazing.OrionGuard.SchemaExport;

/// <summary>
/// Maps a CLR type onto the shape it takes once serialized, so the JSON Schema and the
/// TypeScript exporters agree on what a member is before they disagree on how to spell it.
/// </summary>
internal static class TypeShapes
{
    /// <summary>
    /// The element type when <paramref name="type"/> serializes as a JSON array, otherwise null.
    /// <see cref="string"/> is an <see cref="IEnumerable{T}"/> but serializes as a scalar, and a
    /// dictionary serializes as a JSON object, so both are excluded.
    /// </summary>
    [RequiresUnreferencedCode(SchemaExportAnnotations.UnreferencedCode)]
    internal static Type? GetElementType(Type type)
    {
        if (type == typeof(string) || IsDictionary(type))
        {
            return null;
        }

        if (type.IsArray)
        {
            return type.GetElementType();
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
        {
            return type.GetGenericArguments()[0];
        }

        foreach (var contract in type.GetInterfaces())
        {
            if (contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                return contract.GetGenericArguments()[0];
            }
        }

        return null;
    }

    internal static bool IsDictionary(Type type) => typeof(IDictionary).IsAssignableFrom(type)
        || type.GetInterfaces().Any(contract =>
            contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IDictionary<,>));

    /// <summary>
    /// The JSON Schema primitive type for a scalar, or null when the type is not a scalar and has
    /// to be described as an object, an array, or an enum instead.
    /// </summary>
    internal static string? ScalarJsonType(Type type)
    {
        if (type.IsEnum)
        {
            // An enum has an integer TypeCode but is exported by name, so it is not a scalar here.
            return null;
        }

        return Type.GetTypeCode(type) switch
        {
            TypeCode.Boolean => "boolean",
            TypeCode.Byte or TypeCode.SByte or TypeCode.Int16 or TypeCode.UInt16
                or TypeCode.Int32 or TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64 => "integer",
            TypeCode.Single or TypeCode.Double or TypeCode.Decimal => "number",
            TypeCode.Char or TypeCode.String or TypeCode.DateTime => "string",
            _ => type == typeof(Guid) || type == typeof(Uri) || type == typeof(DateTimeOffset)
                || type == typeof(DateOnly) || type == typeof(TimeOnly) || type == typeof(TimeSpan)
                ? "string"
                : null
        };
    }

    /// <summary>
    /// The JSON Schema <c>format</c> a type carries on its own. OrionGuard has no URL attribute, so
    /// a <see cref="Uri"/> member is the only way it knows a string holds a URL.
    /// </summary>
    internal static string? TypeFormat(Type type)
    {
        if (type == typeof(Guid)) return "uuid";
        if (type == typeof(Uri)) return "uri";
        if (type == typeof(DateTime) || type == typeof(DateTimeOffset)) return "date-time";
        if (type == typeof(DateOnly)) return "date";
        if (type == typeof(TimeOnly)) return "time";
        return null;
    }

    /// <summary>
    /// True when the type gets its own schema definition (and its own TypeScript interface)
    /// because its members carry the rules worth exporting.
    /// </summary>
    [RequiresUnreferencedCode(SchemaExportAnnotations.UnreferencedCode)]
    internal static bool IsExportedObject(Type type) =>
        type != typeof(object)
        && !type.IsEnum
        && ScalarJsonType(type) is null
        && !IsDictionary(type)
        && GetElementType(type) is null;
}
