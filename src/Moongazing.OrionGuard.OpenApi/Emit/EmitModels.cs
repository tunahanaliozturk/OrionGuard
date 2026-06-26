#nullable enable

using System.Collections.Generic;
using System.Collections.Immutable;
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
    /// The specific CLR numeric family of a <see cref="MemberTypeCategory.Numeric"/> member. The emitter
    /// needs this (not just "numeric") to render a bound literal and comparison that compile for the
    /// member's actual type: a <c>decimal</c> bound must be an <c>m</c> literal compared against a
    /// <c>decimal</c>, an unsigned member must not be compared against a signed/over-range literal, and a
    /// <c>float</c>/<c>double</c> bound carries the matching suffix.
    /// </summary>
    internal enum NumericKind
    {
        /// <summary>Not a numeric member (the default for non-numeric categories).</summary>
        None,

        /// <summary>A signed integral type: <c>sbyte</c>, <c>short</c>, <c>int</c>, <c>long</c>.</summary>
        SignedIntegral,

        /// <summary>An unsigned integral type: <c>byte</c>, <c>ushort</c>, <c>uint</c>, <c>ulong</c>.</summary>
        UnsignedIntegral,

        /// <summary><see cref="float"/>.</summary>
        Single,

        /// <summary><see cref="double"/>.</summary>
        Double,

        /// <summary><see cref="decimal"/>.</summary>
        Decimal,
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
            NumericKind numericKind,
            bool memberIsReferenceType,
            OpenApiSchema schema,
            bool required)
        {
            SchemaName = schemaName;
            MemberName = memberName;
            Category = category;
            NumericKind = numericKind;
            MemberIsReferenceType = memberIsReferenceType;
            Schema = schema;
            Required = required;
        }

        /// <summary>The property name as written in the OpenAPI document.</summary>
        public string SchemaName { get; }

        /// <summary>The matched C# member name on the validated type.</summary>
        public string MemberName { get; }

        public MemberTypeCategory Category { get; }

        /// <summary>The specific numeric family when <see cref="Category"/> is
        /// <see cref="MemberTypeCategory.Numeric"/>; <see cref="NumericKind.None"/> otherwise.</summary>
        public NumericKind NumericKind { get; }

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
            string accessibility,
            string validatedTypeFullName,
            ImmutableArray<EnclosingType> enclosingTypes,
            IReadOnlyList<PropertyBinding> properties)
        {
            Namespace = namespaceName;
            ClassName = className;
            Accessibility = accessibility;
            ValidatedTypeFullName = validatedTypeFullName;
            EnclosingTypes = enclosingTypes;
            Properties = properties;
        }

        public string? Namespace { get; }

        public string ClassName { get; }

        /// <summary>The chain of enclosing types from outermost to innermost the generated partial must be
        /// nested inside, each reconstructed as a <c>partial</c> declaration. Empty for a non-nested
        /// target.</summary>
        public ImmutableArray<EnclosingType> EnclosingTypes { get; }

        /// <summary>The accessibility keyword(s) the generated partial repeats so it agrees with the user's
        /// partial declaration (e.g. <c>public</c>, <c>internal</c>).</summary>
        public string Accessibility { get; }

        /// <summary>The fully-qualified name of the type the validator validates (<c>T</c>).</summary>
        public string ValidatedTypeFullName { get; }

        public IReadOnlyList<PropertyBinding> Properties { get; }
    }
}
