using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;

namespace Moongazing.OrionGuard.SchemaExport;

/// <summary>
/// Turns the OrionGuard validation attributes declared on a model into a JSON Schema
/// (draft 2020-12) document, so a frontend validates against the rules the server already
/// declared instead of a hand-written copy of them.
/// </summary>
/// <remarks>
/// <para>
/// Only the rules the exporter can read are in the document: the seven validation attributes in
/// <c>Moongazing.OrionGuard.Attributes</c>. Rules registered on an <c>AbstractValidator&lt;T&gt;</c>
/// or built with <c>Validate.For</c> are C# delegates and are invisible to reflection, so the
/// document never claims to cover them.
/// </para>
/// <para>
/// Any other <c>ValidationAttribute</c> -- a custom predicate, a comparison against a second
/// property -- is listed under <c>x-orionguard-unsupported</c> on the schema that declares the
/// member, so the consumer can see what the document does not enforce.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var schema = JsonSchemaExporter.Export&lt;CreateUserRequest&gt;();
/// File.WriteAllText("create-user.schema.json", schema);
/// </code>
/// </example>
public static class JsonSchemaExporter
{
    private const string Dialect = "https://json-schema.org/draft/2020-12/schema";

    /// <summary>
    /// Unanchored, so it means "holds a non-whitespace character" -- the part of <c>[NotEmpty]</c>
    /// on a string that <c>minLength</c> cannot say.
    /// </summary>
    private const string NonWhitespace = @"\S";

    /// <summary>
    /// Exports <typeparamref name="T"/> as an indented JSON Schema document.
    /// </summary>
    /// <typeparam name="T">The model whose members and validation attributes are exported.</typeparam>
    /// <returns>The schema document. Nested object types are emitted under <c>$defs</c>.</returns>
    [RequiresUnreferencedCode(SchemaExportAnnotations.UnreferencedCode)]
    public static string Export<T>()
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            Export<T>(writer);
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>
    /// Writes the JSON Schema document for <typeparamref name="T"/> to <paramref name="writer"/>.
    /// Use this overload to stream the schema, or to place it inside a larger document.
    /// </summary>
    /// <typeparam name="T">The model whose members and validation attributes are exported.</typeparam>
    /// <param name="writer">The writer the schema object is written to. It is not flushed or disposed.</param>
    /// <exception cref="ArgumentNullException"><paramref name="writer"/> is null.</exception>
    [RequiresUnreferencedCode(SchemaExportAnnotations.UnreferencedCode)]
    public static void Export<T>(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        var model = ExportModel.Build(typeof(T));
        WriteObject(writer, model, model.Root, isRoot: true);
    }

    [RequiresUnreferencedCode(SchemaExportAnnotations.UnreferencedCode)]
    private static void WriteObject(Utf8JsonWriter writer, ExportModel model, Type type, bool isRoot)
    {
        var members = model.MembersOf(type);

        writer.WriteStartObject();

        if (isRoot)
        {
            writer.WriteString("$schema", Dialect);
        }

        writer.WriteString("title", model.Names[type]);
        writer.WriteString("type", "object");

        writer.WriteStartObject("properties");
        foreach (var member in members)
        {
            writer.WritePropertyName(member.Name);
            WriteMember(writer, model, member);
        }
        writer.WriteEndObject();

        if (members.Any(member => member.IsRequired))
        {
            writer.WriteStartArray("required");
            foreach (var member in members.Where(member => member.IsRequired))
            {
                writer.WriteStringValue(member.Name);
            }
            writer.WriteEndArray();
        }

        if (members.Any(member => member.Unsupported.Count > 0))
        {
            writer.WriteStartArray("x-orionguard-unsupported");
            foreach (var member in members)
            {
                foreach (var rule in member.Unsupported)
                {
                    writer.WriteStartObject();
                    writer.WriteString("member", member.Name);
                    writer.WriteString("rule", rule);
                    writer.WriteEndObject();
                }
            }
            writer.WriteEndArray();
        }

        // Every nested object hangs off the root document so the schema stays self-contained.
        if (isRoot && model.Types.Count > 1)
        {
            writer.WriteStartObject("$defs");
            foreach (var nested in model.Types.Skip(1))
            {
                writer.WritePropertyName(model.Names[nested]);
                WriteObject(writer, model, nested, isRoot: false);
            }
            writer.WriteEndObject();
        }

        writer.WriteEndObject();
    }

    [RequiresUnreferencedCode(SchemaExportAnnotations.UnreferencedCode)]
    private static void WriteMember(Utf8JsonWriter writer, ExportModel model, MemberModel member)
    {
        writer.WriteStartObject();

        WriteShape(writer, model, member.Shape, member.Format);

        // A length rule on a collection bounds the number of items, not the number of characters.
        var isCollection = member.Shape.Element is not null;

        if (member.MinLength is { } minLength)
        {
            writer.WriteNumber(isCollection ? "minItems" : "minLength", minLength);
        }

        if (member.MaxLength is { } maxLength)
        {
            writer.WriteNumber(isCollection ? "maxItems" : "maxLength", maxLength);
        }

        WritePatterns(writer, member);

        if (member.Minimum is { } minimum)
        {
            writer.WriteNumber("minimum", minimum);
        }

        if (member.Maximum is { } maximum)
        {
            writer.WriteNumber("maximum", maximum);
        }

        if (member.ExclusiveMinimum is { } exclusiveMinimum)
        {
            writer.WriteNumber("exclusiveMinimum", exclusiveMinimum);
        }

        writer.WriteEndObject();
    }

    /// <summary>
    /// Writes the declared patterns. <c>pattern</c> holds a single expression, so a member with
    /// more than one rule gets them ANDed explicitly instead of losing all but the last.
    /// </summary>
    private static void WritePatterns(Utf8JsonWriter writer, MemberModel member)
    {
        var count = member.Patterns.Count + (member.RequiresNonWhitespace ? 1 : 0);

        if (count == 0)
        {
            return;
        }

        if (count == 1)
        {
            writer.WriteString("pattern", member.RequiresNonWhitespace ? NonWhitespace : member.Patterns[0]);
            return;
        }

        writer.WriteStartArray("allOf");

        if (member.RequiresNonWhitespace)
        {
            WritePattern(writer, NonWhitespace);
        }

        foreach (var pattern in member.Patterns)
        {
            WritePattern(writer, pattern);
        }

        writer.WriteEndArray();
    }

    private static void WritePattern(Utf8JsonWriter writer, string pattern)
    {
        writer.WriteStartObject();
        writer.WriteString("pattern", pattern);
        writer.WriteEndObject();
    }

    /// <summary>
    /// Writes the type keywords for one value into the object the caller has already opened.
    /// </summary>
    [RequiresUnreferencedCode(SchemaExportAnnotations.UnreferencedCode)]
    private static void WriteShape(Utf8JsonWriter writer, ExportModel model, ShapeModel shape, string? format)
    {
        var clrType = shape.ClrType;
        var nullable = shape.IsNullable;

        if (model.Names.TryGetValue(clrType, out var name))
        {
            // The root is the document itself, so a member pointing back at it -- directly or
            // through a cycle -- references the document root rather than a missing definition.
            var pointer = clrType == model.Root ? "#" : $"#/$defs/{name}";

            if (nullable)
            {
                writer.WriteStartArray("anyOf");
                writer.WriteStartObject();
                writer.WriteString("$ref", pointer);
                writer.WriteEndObject();
                writer.WriteStartObject();
                writer.WriteString("type", "null");
                writer.WriteEndObject();
                writer.WriteEndArray();
            }
            else
            {
                writer.WriteString("$ref", pointer);
            }

            return;
        }

        if (shape.Element is { } element)
        {
            WriteType(writer, "array", nullable);
            writer.WriteStartObject("items");
            WriteShape(writer, model, element, TypeShapes.TypeFormat(element.ClrType));
            writer.WriteEndObject();
            return;
        }

        if (clrType.IsEnum)
        {
            // Exported by name, which is what JsonStringEnumConverter writes and what a frontend
            // can read. A numeric enum payload will not match this schema.
            WriteType(writer, "string", nullable);
            writer.WriteStartArray("enum");
            foreach (var value in Enum.GetNames(clrType))
            {
                writer.WriteStringValue(value);
            }
            if (nullable)
            {
                writer.WriteNullValue();
            }
            writer.WriteEndArray();
            return;
        }

        if (TypeShapes.IsDictionary(clrType))
        {
            WriteType(writer, "object", nullable);
            return;
        }

        if (TypeShapes.ScalarJsonType(clrType) is { } scalar)
        {
            WriteType(writer, scalar, nullable);

            if (format is not null)
            {
                writer.WriteString("format", format);
            }

            if (TypeShapes.ContentEncoding(clrType) is { } encoding)
            {
                writer.WriteString("contentEncoding", encoding);
            }
        }

        // Anything left is System.Object: the value is unconstrained and stays that way.
    }

    private static void WriteType(Utf8JsonWriter writer, string jsonType, bool nullable)
    {
        if (!nullable)
        {
            writer.WriteString("type", jsonType);
            return;
        }

        writer.WriteStartArray("type");
        writer.WriteStringValue(jsonType);
        writer.WriteStringValue("null");
        writer.WriteEndArray();
    }
}
