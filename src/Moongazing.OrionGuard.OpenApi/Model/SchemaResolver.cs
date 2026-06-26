#nullable enable

using System.Collections.Generic;
using Moongazing.OrionGuard.OpenApi.Json;

namespace Moongazing.OrionGuard.OpenApi.Model
{
    /// <summary>
    /// The outcome of resolving a JSON pointer or <c>$ref</c> against the document.
    /// </summary>
    internal sealed class ResolveResult
    {
        private ResolveResult(JsonValue? node, string? error)
        {
            Node = node;
            Error = error;
        }

        /// <summary>The resolved node, or <c>null</c> when resolution failed.</summary>
        public JsonValue? Node { get; }

        /// <summary>A human-readable failure reason, or <c>null</c> on success.</summary>
        public string? Error { get; }

        public bool Success => Node is not null;

        public static ResolveResult Ok(JsonValue node) => new ResolveResult(node, null);

        public static ResolveResult Fail(string error) => new ResolveResult(null, error);
    }

    /// <summary>
    /// Resolves JSON pointers and intra-document <c>$ref</c>s against a parsed OpenAPI document.
    /// </summary>
    /// <remarks>
    /// Only same-document references are supported: a pointer must be either a bare fragment
    /// (<c>#/components/schemas/Customer</c>) or a document-relative fragment. External references
    /// (<c>other.yaml#/...</c> or an absolute URI) are reported as failures so the generator can raise
    /// a diagnostic rather than guess.
    /// </remarks>
    internal sealed class SchemaResolver
    {
        private readonly JsonValue _root;

        public SchemaResolver(JsonValue root)
        {
            _root = root;
        }

        /// <summary>
        /// Resolves a JSON pointer such as <c>#/components/schemas/Customer</c> to its node.
        /// </summary>
        public ResolveResult ResolvePointer(string pointer)
        {
            if (string.IsNullOrEmpty(pointer))
            {
                return ResolveResult.Fail("The JSON pointer is empty.");
            }

            string fragment = pointer;

            int hashIndex = fragment.IndexOf('#');
            if (hashIndex < 0)
            {
                return ResolveResult.Fail(
                    $"'{pointer}' is not a fragment pointer. Only same-document pointers beginning with '#/' are supported.");
            }

            // Anything before '#' identifies a different document, which is out of scope.
            if (hashIndex > 0)
            {
                return ResolveResult.Fail(
                    $"'{pointer}' references an external document. Only same-document pointers are supported.");
            }

            fragment = fragment.Substring(hashIndex + 1);

            if (fragment.Length == 0)
            {
                return ResolveResult.Ok(_root);
            }

            if (fragment[0] != '/')
            {
                return ResolveResult.Fail($"'{pointer}' is not a valid JSON pointer (it must start with '#/').");
            }

            string[] tokens = fragment.Substring(1).Split('/');
            JsonValue current = _root;

            foreach (string rawToken in tokens)
            {
                string token = UnescapeToken(rawToken);

                if (!current.IsObject)
                {
                    return ResolveResult.Fail($"'{pointer}' does not resolve: '{token}' has no parent object.");
                }

                var next = current.TryGet(token);
                if (next is null)
                {
                    return ResolveResult.Fail($"'{pointer}' does not resolve: member '{token}' was not found.");
                }

                current = next;
            }

            return ResolveResult.Ok(current);
        }

        /// <summary>
        /// Resolves a schema that may be a local <c>$ref</c>. If the schema is not a reference, it is
        /// returned unchanged. A reference is followed once; a chain of references is followed
        /// transitively, with cycle detection so a self-referential schema cannot loop forever.
        /// </summary>
        public (OpenApiSchema? schema, string? error) ResolveSchema(OpenApiSchema schema)
        {
            if (!schema.IsRef)
            {
                return (schema, null);
            }

            var visited = new HashSet<string>(System.StringComparer.Ordinal);
            OpenApiSchema current = schema;

            while (current.IsRef)
            {
                string reference = current.Ref!;
                if (!visited.Add(reference))
                {
                    return (null, $"The $ref '{reference}' is part of a reference cycle.");
                }

                var resolved = ResolvePointer(reference);
                if (!resolved.Success)
                {
                    return (null, resolved.Error);
                }

                current = OpenApiSchema.Parse(resolved.Node!);
            }

            return (current, null);
        }

        /// <summary>
        /// Reverses the RFC 6901 token escaping: <c>~1</c> becomes <c>/</c> and <c>~0</c> becomes <c>~</c>.
        /// Order matters: <c>~1</c> must be decoded before <c>~0</c>.
        /// </summary>
        private static string UnescapeToken(string token)
        {
            if (token.IndexOf('~') < 0)
            {
                return token;
            }

            return token.Replace("~1", "/").Replace("~0", "~");
        }
    }
}
