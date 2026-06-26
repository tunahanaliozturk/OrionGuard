#nullable enable

using Microsoft.CodeAnalysis;

namespace Moongazing.OrionGuard.OpenApi.Diagnostics
{
    /// <summary>
    /// Diagnostic descriptors for the OpenAPI validator generator. All ids share the OG prefix used by
    /// the rest of OrionGuard's tooling; the OG1xxx band is reserved for this generator (OG0001 is the
    /// existing missing-validation analyzer). Every descriptor is non-fatal: the generator reports and
    /// then skips the offending target so the build never crashes and stays
    /// <c>TreatWarningsAsErrors</c>-survivable for unrelated code.
    /// </summary>
    internal static class OpenApiDiagnostics
    {
        private const string Category = "OrionGuard.OpenApi";

        /// <summary>OG1001: the named OpenAPI document was not supplied as an AdditionalFile.</summary>
        public static readonly DiagnosticDescriptor MissingDocument = new DiagnosticDescriptor(
            id: "OG1001",
            title: "OpenAPI document not found",
            messageFormat: "The OpenAPI document '{0}' named by [OpenApiValidator] was not supplied as an AdditionalFile. Add it via <AdditionalFiles Include=\"...\" /> (or <OpenApiSchema Include=\"...\" />) in the consuming project.",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>OG1002: the document could not be parsed as JSON.</summary>
        public static readonly DiagnosticDescriptor InvalidDocument = new DiagnosticDescriptor(
            id: "OG1002",
            title: "OpenAPI document could not be parsed",
            messageFormat: "The OpenAPI document '{0}' could not be parsed as JSON: {1}. YAML documents are not supported yet; convert the document to JSON.",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>OG1003: the JSON pointer did not resolve to a schema.</summary>
        public static readonly DiagnosticDescriptor UnresolvablePointer = new DiagnosticDescriptor(
            id: "OG1003",
            title: "OpenAPI schema pointer did not resolve",
            messageFormat: "The pointer '{0}' did not resolve to a schema in '{1}': {2}",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>OG1004: a $ref inside the document could not be resolved.</summary>
        public static readonly DiagnosticDescriptor UnresolvableRef = new DiagnosticDescriptor(
            id: "OG1004",
            title: "OpenAPI $ref did not resolve",
            messageFormat: "A $ref in the schema for '{0}' could not be resolved: {1}",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>OG1005: the target type is not a partial class.</summary>
        public static readonly DiagnosticDescriptor TargetNotPartial = new DiagnosticDescriptor(
            id: "OG1005",
            title: "[OpenApiValidator] target is not a partial class",
            messageFormat: "'{0}' is annotated with [OpenApiValidator] but is not a partial class, so no validator was generated. Add the 'partial' modifier.",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        /// <summary>OG1006: an unsupported construct was encountered and skipped.</summary>
        public static readonly DiagnosticDescriptor UnsupportedConstruct = new DiagnosticDescriptor(
            id: "OG1006",
            title: "Unsupported OpenAPI construct skipped",
            messageFormat: "The schema for '{0}' uses '{1}', which the OpenAPI validator generator does not support yet (polymorphism and composition are deferred). The construct was skipped; the rest of the schema was still enforced.",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        /// <summary>OG1007: an integer keyword value was out of the supported range (or not an integer).</summary>
        public static readonly DiagnosticDescriptor InvalidKeywordValue = new DiagnosticDescriptor(
            id: "OG1007",
            title: "OpenAPI numeric keyword value is out of range",
            messageFormat: "The keyword '{0}' on the schema for '{1}' has value '{2}' which {3}. The constraint was skipped; the rest of the schema was still enforced.",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        /// <summary>
        /// OG1008: the annotated type does not participate in the OrionGuard <c>IValidator&lt;T&gt;</c>
        /// contract, so the validated type could not be inferred and no validator was generated.
        /// </summary>
        public static readonly DiagnosticDescriptor MissingValidatorContract = new DiagnosticDescriptor(
            id: "OG1008",
            title: "[OpenApiValidator] target does not implement IValidator<T>",
            messageFormat: "'{0}' is annotated with [OpenApiValidator] but does not implement Moongazing.OrionGuard.DependencyInjection.IValidator<T> (directly or through AbstractValidator<T> / FluentStyleValidator<T>), so the validated type could not be inferred and no validator was generated. Make it implement IValidator<T> for the type it validates.",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        /// <summary>
        /// OG1009: the document name in <c>[OpenApiValidator]</c> matched more than one AdditionalFile, so
        /// the match was ambiguous and no validator was generated.
        /// </summary>
        public static readonly DiagnosticDescriptor AmbiguousDocument = new DiagnosticDescriptor(
            id: "OG1009",
            title: "OpenAPI document name is ambiguous",
            messageFormat: "The document name '{0}' named by [OpenApiValidator] matched more than one AdditionalFile ({1}). Use a more specific relative path so exactly one file matches.",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>
        /// OG1010: the target type, or a type it is nested inside, is generic. Reconstructing a generic
        /// partial with its type parameters and constraints correctly is not supported in this version, so
        /// the generator skips it rather than emitting a partial whose type parameters or constraints do not
        /// match the user's declaration (which would not compile).
        /// </summary>
        public static readonly DiagnosticDescriptor GenericTargetUnsupported = new DiagnosticDescriptor(
            id: "OG1010",
            title: "[OpenApiValidator] does not support generic target types",
            messageFormat: "'{0}' is a generic [OpenApiValidator] target (or is nested inside a generic type), which the OpenAPI validator generator does not support yet, so no validator was generated. Move the validator to a non-generic type.",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        /// <summary>
        /// OG1011: a nested <c>[OpenApiValidator]</c> target, or one of the types it is declared inside, is
        /// not <c>partial</c>. The generator reopens the target and every enclosing type as a nested
        /// <c>partial</c> declaration to land the generated validator in the right place; reopening a
        /// non-partial type with a <c>partial</c> declaration is a consumer compile error (CS0260), so the
        /// target and every enclosing type must be declared <c>partial</c>. The offending type is named and
        /// generation is skipped rather than emitting code that would not compile.
        /// </summary>
        public static readonly DiagnosticDescriptor NestedTargetNotPartial = new DiagnosticDescriptor(
            id: "OG1011",
            title: "OpenApiValidator nested target and all its enclosing types must be declared partial",
            messageFormat: "OpenApiValidator nested target and all its enclosing types must be declared partial, but '{0}' is not, so no validator was generated. Add the 'partial' modifier to '{0}'.",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        /// <summary>
        /// OG1012: a type the nested <c>[OpenApiValidator]</c> target is declared inside is a kind the
        /// generator cannot reopen as a <c>partial</c> declaration. The emitter reproduces each enclosing
        /// type's keyword verbatim (<c>class</c> / <c>struct</c> / <c>record</c> / <c>record struct</c> /
        /// <c>interface</c>); any other kind cannot be reproduced, so the target is skipped rather than
        /// emitting a partial with a wrong or missing keyword that would not compile.
        /// </summary>
        public static readonly DiagnosticDescriptor EnclosingTypeKindUnsupported = new DiagnosticDescriptor(
            id: "OG1012",
            title: "OpenApiValidator enclosing type kind cannot be reopened",
            messageFormat: "The [OpenApiValidator] target is nested inside '{0}', whose type kind the generator cannot reopen as a partial declaration. Only class, struct, record, record struct, and interface enclosing types are supported, so no validator was generated.",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);
    }
}
