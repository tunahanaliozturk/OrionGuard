#nullable enable

using System.Collections.Generic;

namespace Moongazing.OrionGuard.OpenApi.Json
{
    /// <summary>
    /// The kind of a parsed <see cref="JsonValue"/> node.
    /// </summary>
    internal enum JsonKind
    {
        Object,
        Array,
        String,
        Number,
        Boolean,
        Null,
    }

    /// <summary>
    /// A minimal, self-contained JSON DOM node. The generator bundles its own parser rather than
    /// taking a NuGet dependency, so a consuming build never has to resolve an analyzer-time package
    /// for the small JSON subset OpenAPI documents need (objects, arrays, strings, numbers, booleans,
    /// null). Only read access is provided; the tree is immutable once parsed.
    /// </summary>
    internal sealed class JsonValue
    {
        private readonly Dictionary<string, JsonValue>? _object;
        private readonly List<JsonValue>? _array;
        private readonly string? _string;
        private readonly double _number;
        private readonly bool _boolean;

        /// <summary>The raw lexeme for a number token, preserved so integer vs. fractional intent survives.</summary>
        private readonly string? _rawNumber;

        private JsonValue(JsonKind kind, Dictionary<string, JsonValue>? obj, List<JsonValue>? arr,
            string? str, double number, string? rawNumber, bool boolean)
        {
            Kind = kind;
            _object = obj;
            _array = arr;
            _string = str;
            _number = number;
            _rawNumber = rawNumber;
            _boolean = boolean;
        }

        public JsonKind Kind { get; }

        public static JsonValue NewObject(Dictionary<string, JsonValue> members) =>
            new JsonValue(JsonKind.Object, members, null, null, 0, null, false);

        public static JsonValue NewArray(List<JsonValue> items) =>
            new JsonValue(JsonKind.Array, null, items, null, 0, null, false);

        public static JsonValue NewString(string value) =>
            new JsonValue(JsonKind.String, null, null, value, 0, null, false);

        public static JsonValue NewNumber(double value, string raw) =>
            new JsonValue(JsonKind.Number, null, null, null, value, raw, false);

        public static JsonValue NewBoolean(bool value) =>
            new JsonValue(JsonKind.Boolean, null, null, null, 0, null, value);

        public static JsonValue NewNull() =>
            new JsonValue(JsonKind.Null, null, null, null, 0, null, false);

        public bool IsObject => Kind == JsonKind.Object;

        public bool IsArray => Kind == JsonKind.Array;

        /// <summary>Object members. Empty when this node is not an object.</summary>
        public IReadOnlyDictionary<string, JsonValue> Members =>
            _object ?? EmptyObject;

        /// <summary>Array items. Empty when this node is not an array.</summary>
        public IReadOnlyList<JsonValue> Items =>
            _array ?? (IReadOnlyList<JsonValue>)System.Array.Empty<JsonValue>();

        /// <summary>The string value, or <c>null</c> when this node is not a string.</summary>
        public string? AsString() => Kind == JsonKind.String ? _string : null;

        /// <summary>The numeric value as a double, or <c>null</c> when this node is not a number.</summary>
        public double? AsNumber() => Kind == JsonKind.Number ? _number : (double?)null;

        /// <summary>The raw numeric lexeme (e.g. <c>"42"</c> or <c>"3.5"</c>), or <c>null</c> when not a number.</summary>
        public string? RawNumber() => Kind == JsonKind.Number ? _rawNumber : null;

        /// <summary>The boolean value, or <c>null</c> when this node is not a boolean.</summary>
        public bool? AsBoolean() => Kind == JsonKind.Boolean ? _boolean : (bool?)null;

        /// <summary>
        /// Returns the named member when this node is an object and the member exists; otherwise <c>null</c>.
        /// </summary>
        public JsonValue? TryGet(string name)
        {
            if (_object is not null && _object.TryGetValue(name, out var value))
            {
                return value;
            }

            return null;
        }

        private static readonly Dictionary<string, JsonValue> EmptyObject = new Dictionary<string, JsonValue>();
    }
}
