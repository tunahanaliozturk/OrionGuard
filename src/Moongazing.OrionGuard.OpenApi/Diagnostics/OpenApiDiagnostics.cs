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
    }
}
