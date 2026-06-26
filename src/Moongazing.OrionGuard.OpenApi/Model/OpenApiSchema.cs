#nullable enable

using System.Collections.Generic;
using System.Globalization;
using Moongazing.OrionGuard.OpenApi.Json;

namespace Moongazing.OrionGuard.OpenApi.Model
{
    /// <summary>
    /// A single named property of an object schema: the property name plus its schema.
    /// </summary>
    internal sealed class OpenApiProperty
    {
        public OpenApiProperty(string name, OpenApiSchema schema)
        {
            Name = name;
            Schema = schema;
        }

        public string Name { get; }

        public OpenApiSchema Schema { get; }
    }

    /// <summary>
    /// A keyword value the parser could read syntactically but had to reject semantically (for example an
    /// integer keyword whose value overflows <see cref="int"/>). Carried on the schema so the generator can
    /// raise a diagnostic instead of silently wrapping or dropping the value.
    /// </summary>
    internal readonly struct SchemaIssue
    {
        public SchemaIssue(string keyword, string rawValue, string reason)
        {
            Keyword = keyword;
            RawValue = rawValue;
            Reason = reason;
        }

        public string Keyword { get; }

        public string RawValue { get; }

        public string Reason { get; }
    }

    /// <summary>
    /// The bounded subset of an OpenAPI 3 / JSON Schema object that the generator understands.
    /// Anything outside this subset is preserved as <see cref="HasUnsupportedConstruct"/> so the
    /// generator can raise an informational diagnostic instead of silently dropping a constraint.
    /// </summary>
    /// <remarks>
    /// Deliberately out of scope for this release and surfaced via <see cref="HasUnsupportedConstruct"/>:
    /// <c>discriminator</c>, <c>oneOf</c>, <c>anyOf</c>, and <c>allOf</c> (polymorphism / composition).
    /// </remarks>
    internal sealed class OpenApiSchema
    {
        private OpenApiSchema()
        {
            Properties = new List<OpenApiProperty>();
            Required = new HashSet<string>(System.StringComparer.Ordinal);
            EnumValues = new List<JsonValue>();
            Issues = new List<SchemaIssue>();
        }

        /// <summary>Keyword values rejected during parse (e.g. an out-of-range integer keyword).</summary>
        public List<SchemaIssue> Issues { get; }

        /// <summary>A local <c>$ref</c> (for example <c>#/components/schemas/Address</c>), or <c>null</c>.</summary>
        public string? Ref { get; private set; }

        /// <summary>The declared <c>type</c> (string, integer, number, boolean, array, object), or <c>null</c>.</summary>
        public string? Type { get; private set; }

        /// <summary>The declared <c>format</c> (email, uuid, date-time, uri, ...), or <c>null</c>.</summary>
        public string? Format { get; private set; }

        public bool Nullable { get; private set; }

        // String constraints.
        public int? MinLength { get; private set; }
        public int? MaxLength { get; private set; }
        public string? Pattern { get; private set; }

        // Numeric constraints. Stored as the raw lexeme so the emitted literal keeps the document's
        // integer-vs-decimal intent and exact value (no float round-tripping in the generated code).
        public string? Minimum { get; private set; }
        public string? Maximum { get; private set; }
        public bool ExclusiveMinimum { get; private set; }
        public bool ExclusiveMaximum { get; private set; }

        // Array constraints.
        public int? MinItems { get; private set; }
        public int? MaxItems { get; private set; }
        public OpenApiSchema? Items { get; private set; }

        // Object constraints.
        public List<OpenApiProperty> Properties { get; }
        public HashSet<string> Required { get; }

        /// <summary>The allowed values from an <c>enum</c> declaration; empty when none.</summary>
        public List<JsonValue> EnumValues { get; }

        /// <summary>
        /// True when the schema declares a construct the generator does not implement
        /// (<c>discriminator</c>, <c>oneOf</c>, <c>anyOf</c>, <c>allOf</c>). Set so the generator can
        /// raise OG1006 and skip rather than emit a partial, misleading validator.
        /// </summary>
        public bool HasUnsupportedConstruct { get; private set; }

        /// <summary>The name of the first unsupported construct encountered, for the diagnostic message.</summary>
        public string? UnsupportedConstructName { get; private set; }

        public bool IsRef => Ref is not null;

        /// <summary>
        /// Parses a single schema node. Child schemas (properties, array items) are parsed eagerly so
        /// the whole bounded subtree is materialized; <c>$ref</c> nodes are captured but not followed
        /// here (resolution is the <see cref="SchemaResolver"/>'s job, against the whole document).
        /// </summary>
        public static OpenApiSchema Parse(JsonValue node)
        {
            var schema = new OpenApiSchema();

            if (!node.IsObject)
            {
                // A non-object schema node (for example a stray boolean) carries no constraints we model.
                return schema;
            }

            var refNode = node.TryGet("$ref");
            if (refNode is not null && refNode.AsString() is string refValue)
            {
                schema.Ref = refValue;
                // A $ref node in OpenAPI is a pure reference; sibling keywords are ignored by spec.
                return schema;
            }

            schema.Type = node.TryGet("type")?.AsString();
            schema.Format = node.TryGet("format")?.AsString();
            schema.Nullable = node.TryGet("nullable")?.AsBoolean() ?? false;

            schema.MinLength = ReadInt(node.TryGet("minLength"), "minLength", schema.Issues);
            schema.MaxLength = ReadInt(node.TryGet("maxLength"), "maxLength", schema.Issues);
            schema.Pattern = node.TryGet("pattern")?.AsString();

            schema.Minimum = node.TryGet("minimum")?.RawNumber();
            schema.Maximum = node.TryGet("maximum")?.RawNumber();
            (schema.ExclusiveMinimum, var exclMinBound) = ReadExclusive(node.TryGet("exclusiveMinimum"));
            if (exclMinBound is not null)
            {
                schema.Minimum = exclMinBound;
            }

            (schema.ExclusiveMaximum, var exclMaxBound) = ReadExclusive(node.TryGet("exclusiveMaximum"));
            if (exclMaxBound is not null)
            {
                schema.Maximum = exclMaxBound;
            }

            schema.MinItems = ReadInt(node.TryGet("minItems"), "minItems", schema.Issues);
            schema.MaxItems = ReadInt(node.TryGet("maxItems"), "maxItems", schema.Issues);

            var itemsNode = node.TryGet("items");
            if (itemsNode is not null)
            {
                schema.Items = Parse(itemsNode);
            }

            var enumNode = node.TryGet("enum");
            if (enumNode is not null && enumNode.IsArray)
            {
                foreach (var value in enumNode.Items)
                {
                    schema.EnumValues.Add(value);
                }
            }

            var requiredNode = node.TryGet("required");
            if (requiredNode is not null && requiredNode.IsArray)
            {
                foreach (var value in requiredNode.Items)
                {
                    if (value.AsString() is string requiredName)
                    {
                        schema.Required.Add(requiredName);
                    }
                }
            }

            var propertiesNode = node.TryGet("properties");
            if (propertiesNode is not null && propertiesNode.IsObject)
            {
                foreach (var member in propertiesNode.Members)
                {
                    schema.Properties.Add(new OpenApiProperty(member.Key, Parse(member.Value)));
                }
            }

            // Detect composition / polymorphism keywords we deliberately do not implement.
            foreach (var unsupported in UnsupportedKeywords)
            {
                if (node.TryGet(unsupported) is not null)
                {
                    schema.HasUnsupportedConstruct = true;
                    schema.UnsupportedConstructName = unsupported;
                    break;
                }
            }

            return schema;
        }

        private static int? ReadInt(JsonValue? value, string keyword, List<SchemaIssue> issues)
        {
            if (value is null)
            {
                return null;
            }

            var raw = value.RawNumber();
            if (raw is null)
            {
                return null;
            }

            // Integer keywords (minLength, maxLength, minItems, maxItems) bound a string Length or a
            // collection Count, both of which are int-domain. Parse the lexeme into a wide decimal (no
            // floating round-off for large integers, unlike double), tolerate a "1.0"-style integral
            // value, then range-check against int. A truncating or wrapping value is rejected with a
            // recorded issue rather than silently narrowed (e.g. (int)5000000000 wrapping to 705032704).
            if (!decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal parsed))
            {
                issues.Add(new SchemaIssue(keyword, raw, "is not a valid number"));
                return null;
            }

            decimal truncated = decimal.Truncate(parsed);
            if (truncated != parsed)
            {
                issues.Add(new SchemaIssue(keyword, raw, "is not an integer"));
                return null;
            }

            if (truncated < int.MinValue || truncated > int.MaxValue)
            {
                issues.Add(new SchemaIssue(
                    keyword, raw, $"is outside the supported range [{int.MinValue}, {int.MaxValue}]"));
                return null;
            }

            return (int)truncated;
        }

        /// <summary>
        /// Reads an <c>exclusiveMinimum</c>/<c>exclusiveMaximum</c> keyword across both spec styles:
        /// OpenAPI 3.0 uses a boolean flag that modifies the sibling minimum/maximum, while JSON Schema
        /// 2020-12 (OpenAPI 3.1) uses a number that is itself the exclusive bound. Returns the flag plus,
        /// for the numeric form, the bound value to use.
        /// </summary>
        private static (bool exclusive, string? bound) ReadExclusive(JsonValue? value)
        {
            if (value is null)
            {
                return (false, null);
            }

            if (value.AsBoolean() is bool flag)
            {
                return (flag, null);
            }

            if (value.RawNumber() is string raw)
            {
                return (true, raw);
            }

            return (false, null);
        }

        private static readonly string[] UnsupportedKeywords =
        {
            "discriminator",
            "oneOf",
            "anyOf",
            "allOf",
        };
    }
}
