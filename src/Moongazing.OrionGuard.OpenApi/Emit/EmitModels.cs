#nullable enable

using System.Collections.Generic;
using Moongazing.OrionGuard.OpenApi.Model;

namespace Moongazing.OrionGuard.OpenApi.Emit
{
    /// <summary>
    /// The category of a validated member's C# type. The emitter only generates a constraint when the
    /// member's category supports it (string checks for strings, numeric comparisons for numbers, count
    /// checks for collections), so the generated code always compiles even if the document and the POCO
    /// disagree about a property's shape.
    /// </summary>
    internal enum MemberTypeCategory
    {
        /// <summary>A reference type that is not a string or collection (a nested object, typically).</summary>
        Object,
        String,
        /// <summary>An integral or floating numeric type (int, long, decimal, double, ...), nullable or not.</summary>
        Numeric,
        Boolean,
        /// <summary>An <see cref="System.Collections.IEnumerable"/> that is not a string.</summary>
        Collection,
        /// <summary>Anything else; the emitter treats it conservatively and skips type-specific checks.</summary>
        Other,
    }

    /// <summary>
    /// Binds an OpenAPI schema property to the concrete C# member the generated validator will read.
    /// The generator resolves the member from the validated type's semantic model so the emitted code
    /// only ever references members that actually exist (keeping the consumer build warning-clean).
    /// </summary>
    internal sealed class PropertyBinding
    {
        public PropertyBinding(
            string schemaName,
            string memberName,
            MemberTypeCategory category,
            bool memberIsReferenceType,
            OpenApiSchema schema,
            bool required)
        {
            SchemaName = schemaName;
            MemberName = memberName;
            Category = category;
            MemberIsReferenceType = memberIsReferenceType;
            Schema = schema;
            Required = required;
        }

        /// <summary>The property name as written in the OpenAPI document.</summary>
        public string SchemaName { get; }

        /// <summary>The matched C# member name on the validated type.</summary>
        public string MemberName { get; }

        public MemberTypeCategory Category { get; }

        /// <summary>Whether the member can be null at runtime (reference type or <see cref="System.Nullable{T}"/>).</summary>
        public bool MemberIsReferenceType { get; }

        /// <summary>The resolved schema for this property (any top-level <c>$ref</c> already followed).</summary>
        public OpenApiSchema Schema { get; }

        /// <summary>Whether the owning object schema lists this property in its <c>required</c> array.</summary>
        public bool Required { get; }
    }

    /// <summary>
    /// Everything the emitter needs about one <c>[OpenApiValidator]</c> target: the partial type to
    /// extend and the bound, resolved property set.
    /// </summary>
    internal sealed class ValidatorEmitModel
    {
        public ValidatorEmitModel(
            string? namespaceName,
            string className,
            string validatedTypeFullName,
            IReadOnlyList<PropertyBinding> properties)
        {
            Namespace = namespaceName;
            ClassName = className;
            ValidatedTypeFullName = validatedTypeFullName;
            Properties = properties;
        }

        public string? Namespace { get; }

        public string ClassName { get; }

        /// <summary>The fully-qualified name of the type the validator validates (<c>T</c>).</summary>
        public string ValidatedTypeFullName { get; }

        public IReadOnlyList<PropertyBinding> Properties { get; }
    }
}
