#nullable enable

using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Moongazing.OrionGuard.Generators
{
    /// <summary>
    /// Roslyn analyzer that flags public instance properties on types decorated with
    /// <c>[GenerateValidator]</c> that lack any OrionGuard validation attribute.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Emits the diagnostic <c>OG0001</c> (category <c>Usage</c>, severity
    /// <see cref="DiagnosticSeverity.Warning"/>) at the location of each unvalidated
    /// property. Opt-in via <c>[GenerateValidator]</c>: types that are not declared as
    /// validator targets are ignored so the analyzer never nags general-purpose DTOs.
    /// </para>
    /// <para>
    /// <b>Scope.</b> Only public instance properties with a public getter are considered.
    /// <c>static</c>, <c>init</c>-only, indexer, and compiler-generated properties are
    /// skipped. The analyzer recognises any attribute in the
    /// <c>Moongazing.OrionGuard.Attributes</c> namespace (<c>NotNull</c>, <c>NotEmpty</c>,
    /// <c>Length</c>, <c>Email</c>, <c>Range</c>, <c>Regex</c>, <c>Positive</c>, etc.).
    /// </para>
    /// </remarks>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class MissingValidationAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "OG0001";

        private const string ValidatorAttributeFullName =
            "Moongazing.OrionGuard.Generators.GenerateValidatorAttribute";

        private const string ValidationAttributeNamespace =
            "Moongazing.OrionGuard.Attributes";

        private static readonly LocalizableString Title =
            "Property has no OrionGuard validation attribute";

        private static readonly LocalizableString MessageFormat =
            "Property '{0}' on '{1}' has no OrionGuard validation attribute and will be silently accepted by the generated validator";

        private static readonly LocalizableString Description =
            "Types decorated with [GenerateValidator] should mark every public property with at least one validation attribute (e.g. [NotNull], [Email]) or explicitly opt out. Silent acceptance of unvalidated input is a common source of production bugs.";

        private static readonly DiagnosticDescriptor Rule = new(
            id: DiagnosticId,
            title: Title,
            messageFormat: MessageFormat,
            category: "Usage",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: Description,
            helpLinkUri: "https://github.com/tunahanaliozturk/OrionGuard/blob/master/docs/FEATURES-v6.md");

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
            ImmutableArray.Create(Rule);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
        }

        private static void AnalyzeNamedType(SymbolAnalysisContext context)
        {
            var type = (INamedTypeSymbol)context.Symbol;

            if (!HasAttribute(type, ValidatorAttributeFullName))
                return;

            foreach (var member in type.GetMembers())
            {
                if (member is not IPropertySymbol property) continue;
                if (!IsCandidate(property)) continue;
                if (HasAnyValidationAttribute(property)) continue;

                var location = property.Locations.FirstOrDefault() ?? Location.None;
                context.ReportDiagnostic(
                    Diagnostic.Create(Rule, location, property.Name, type.Name));
            }
        }

        private static bool IsCandidate(IPropertySymbol property) =>
            property.DeclaredAccessibility == Accessibility.Public &&
            !property.IsStatic &&
            !property.IsIndexer &&
            !property.IsImplicitlyDeclared &&
            property.GetMethod is not null &&
            property.GetMethod.DeclaredAccessibility == Accessibility.Public;

        private static bool HasAttribute(ISymbol symbol, string fullName)
        {
            foreach (var attr in symbol.GetAttributes())
            {
                var cls = attr.AttributeClass;
                if (cls is null) continue;
                if (cls.ToDisplayString() == fullName) return true;
            }
            return false;
        }

        private static bool HasAnyValidationAttribute(ISymbol symbol)
        {
            foreach (var attr in symbol.GetAttributes())
            {
                var cls = attr.AttributeClass;
                if (cls?.ContainingNamespace is null) continue;

                if (cls.ContainingNamespace.ToDisplayString() == ValidationAttributeNamespace)
                    return true;
            }
            return false;
        }
    }
}
