#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Moongazing.OrionGuard.OpenApi.Diagnostics;
using Moongazing.OrionGuard.OpenApi.Emit;
using Moongazing.OrionGuard.OpenApi.Json;
using Moongazing.OrionGuard.OpenApi.Model;

namespace Moongazing.OrionGuard.OpenApi
{
    /// <summary>
    /// Incremental source generator that turns an OpenAPI 3 schema into an OrionGuard validator.
    /// For a partial class annotated <c>[OpenApiValidator("openapi.json", "#/components/schemas/Customer")]</c>
    /// it reads the document (supplied as an AdditionalFile), resolves the pointer and any intra-document
    /// <c>$ref</c>, and emits a partial implementing
    /// <c>Moongazing.OrionGuard.DependencyInjection.IValidator&lt;T&gt;</c> that enforces the schema's
    /// constraints.
    /// </summary>
    /// <remarks>
    /// JSON OpenAPI documents are supported. YAML is deferred (a YAML reader would be a heavy, hard to
    /// bundle analyzer dependency); the generator raises OG1002 for a non-JSON document. Polymorphism
    /// (<c>discriminator</c> / <c>oneOf</c> / <c>anyOf</c> / <c>allOf</c>) is deferred and surfaced as
    /// OG1006 rather than half-implemented.
    /// </remarks>
    [Generator(LanguageNames.CSharp)]
    public sealed class OpenApiValidatorGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // Step 1: inject the marker attribute.
            context.RegisterPostInitializationOutput(static ctx =>
                ctx.AddSource(
                    OpenApiValidatorAttributeSource.HintName,
                    SourceText.From(OpenApiValidatorAttributeSource.Source, Encoding.UTF8)));

            // Step 2: find every [OpenApiValidator] target and project it to a pure value.
            IncrementalValuesProvider<OpenApiValidatorTarget> targets = context.SyntaxProvider
                .ForAttributeWithMetadataName(
                    OpenApiValidatorAttributeSource.FullName,
                    predicate: static (node, _) => node is ClassDeclarationSyntax,
                    transform: static (ctx, ct) => Extract(ctx, ct))
                .Where(static t => t is not null)!;

            // Step 3: capture each AdditionalFile as a value (file name + content) once.
            IncrementalValuesProvider<OpenApiDocument> documents = context.AdditionalTextsProvider
                .Select(static (text, ct) =>
                {
                    var content = text.GetText(ct)?.ToString();
                    var fileName = System.IO.Path.GetFileName(text.Path);
                    return new OpenApiDocument(fileName, text.Path, content);
                });

            var collectedDocuments = documents.Collect();

            // Step 4: emit a validator (or diagnostics) per target, against the collected documents.
            var combined = targets.Combine(collectedDocuments);
            context.RegisterSourceOutput(combined, static (spc, pair) =>
                Execute(spc, pair.Left, pair.Right));
        }

        private static OpenApiValidatorTarget? Extract(GeneratorAttributeSyntaxContext context, CancellationToken ct)
        {
            if (context.TargetSymbol is not INamedTypeSymbol typeSymbol)
            {
                return null;
            }

            ct.ThrowIfCancellationRequested();

            var attribute = context.Attributes.FirstOrDefault();
            if (attribute is null || attribute.ConstructorArguments.Length < 2)
            {
                return null;
            }

            string? documentPath = attribute.ConstructorArguments[0].Value as string;
            string? schemaPointer = attribute.ConstructorArguments[1].Value as string;
            if (documentPath is null || schemaPointer is null)
            {
                return null;
            }

            // The target must itself be declared partial to be extended. Check every source declaration of
            // the symbol (not just the node carrying the attribute) so a type that is partial in one file
            // but non-partial in another is correctly treated as not reopenable.
            bool isPartial = IsDeclaredPartial(typeSymbol);

            string? namespaceName = typeSymbol.ContainingNamespace.IsGlobalNamespace
                ? null
                : typeSymbol.ContainingNamespace.ToDisplayString();

            // Capture the chain of enclosing types (outermost first) so a nested target's partial can be
            // reconstructed inside the correct nesting. A namespace- or global-scope target has none.
            ImmutableArray<EnclosingType> enclosingTypes = CaptureEnclosingTypes(typeSymbol);

            // The generator cannot reconstruct a generic partial's type parameters and constraints
            // correctly yet, so a generic target (or a target nested inside a generic type) is flagged here
            // and skipped with OG1010 in Execute rather than emitting a non-compiling partial.
            bool isGeneric = IsGenericTargetOrNesting(typeSymbol);

            // Infer the validated type T. Prefer an explicit IValidator<T>/AbstractValidator<T>/
            // FluentStyleValidator<T> base or interface; fall back to the only candidate the document
            // names. If none is found, validate the annotated type's own members (it is a DTO itself).
            (string validatedTypeFullName, bool hasValidatedType, ImmutableArray<MemberShape> members) =
                ResolveValidatedType(typeSymbol);

            var location = typeSymbol.Locations.FirstOrDefault(l => l.IsInSource) ?? Location.None;
            var span = location.SourceSpan;
            string locationPath = location.SourceTree?.FilePath ?? string.Empty;

            // The generated partial must declare the same accessibility as the user's partial, or the two
            // declarations conflict (CS0262) and the consumer build fails. A nested type can be private /
            // protected; only public and internal are meaningful at namespace scope, but capture the full
            // keyword so a nested target is handled too.
            string accessibility = AccessibilityKeyword(typeSymbol.DeclaredAccessibility);

            return new OpenApiValidatorTarget(
                namespaceName,
                typeSymbol.Name,
                validatedTypeFullName,
                isPartial,
                hasValidatedType,
                accessibility,
                isGeneric,
                documentPath,
                schemaPointer,
                members,
                enclosingTypes,
                locationPath,
                span.Start,
                span.Length);
        }

        /// <summary>
        /// Walks the target's containing-type chain and returns each enclosing type as an
        /// <see cref="EnclosingType"/> (keyword + simple name), ordered outermost first so the emitter can
        /// open the nesting from the namespace inward. Returns an empty array for a target declared directly
        /// in a namespace or at global scope.
        /// </summary>
        private static ImmutableArray<EnclosingType> CaptureEnclosingTypes(INamedTypeSymbol typeSymbol)
        {
            INamedTypeSymbol? containing = typeSymbol.ContainingType;
            if (containing is null)
            {
                return ImmutableArray<EnclosingType>.Empty;
            }

            // Collect inner-to-outer, then reverse so the outermost enclosing type is emitted first. Each
            // link carries whether the user declared it partial: the emitter may only reopen a partial
            // enclosing type, so a non-partial one is reported (OG1011) and generation is skipped.
            var stack = new List<EnclosingType>();
            for (INamedTypeSymbol? current = containing; current is not null; current = current.ContainingType)
            {
                stack.Add(new EnclosingType(TypeKeyword(current), current.Name, IsDeclaredPartial(current)));
            }

            stack.Reverse();
            return stack.ToImmutableArray();
        }

        /// <summary>
        /// Scans a nested target's declaring-type chain for the first link that is not declared
        /// <c>partial</c> and so cannot be reopened. Enclosing types are checked outermost first (the order
        /// the emitter would open them), then the target itself; the first non-partial type's name is
        /// returned via <paramref name="offendingType"/>. Returns <c>false</c> when the target and every
        /// enclosing type are partial (the whole chain is reopenable). Only meaningful for a nested target;
        /// a top-level target's partiality is handled directly against <see cref="OpenApiValidatorTarget.IsPartial"/>.
        /// </summary>
        private static bool TryFindNonPartialLink(OpenApiValidatorTarget target, out string offendingType)
        {
            foreach (var enclosing in target.EnclosingTypes)
            {
                if (!enclosing.IsPartial)
                {
                    offendingType = enclosing.Name;
                    return true;
                }
            }

            if (!target.IsPartial)
            {
                offendingType = target.ClassName;
                return true;
            }

            offendingType = string.Empty;
            return false;
        }

        /// <summary>
        /// True when every source declaration of <paramref name="type"/> carries the <c>partial</c>
        /// modifier, i.e. the type can be safely reopened with another <c>partial</c> declaration. A type
        /// with no source declarations (purely metadata) is treated as not partial: it lives in another
        /// assembly and cannot be reopened. Checking <em>all</em> declarations is deliberate: a type whose
        /// declarations disagree about <c>partial</c> is itself a consumer compile error, and adding our own
        /// partial would only compound it, so we skip and let the user's own error surface.
        /// </summary>
        private static bool IsDeclaredPartial(INamedTypeSymbol type)
        {
            var references = type.DeclaringSyntaxReferences;
            if (references.Length == 0)
            {
                return false;
            }

            foreach (var reference in references)
            {
                if (reference.GetSyntax() is not TypeDeclarationSyntax declaration
                    || !declaration.Modifiers.Any(m => m.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PartialKeyword)))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// The C# type keyword to repeat for an enclosing type's <c>partial</c> declaration. Records are
        /// distinguished from plain classes/structs so a <c>partial record</c> over a user <c>record</c>
        /// declaration agrees (a <c>partial class</c> over a <c>record</c> is a compile error).
        /// </summary>
        private static string TypeKeyword(INamedTypeSymbol type)
        {
            if (type.IsRecord)
            {
                return type.TypeKind == TypeKind.Struct ? "record struct" : "record";
            }

            return type.TypeKind == TypeKind.Struct ? "struct" : "class";
        }

        /// <summary>
        /// True when the target type itself is generic, or any type it is nested inside is generic. A
        /// generic enclosing type means the target's full nesting cannot be reconstructed without that
        /// type's parameters, so it is treated the same as a generic target.
        /// </summary>
        private static bool IsGenericTargetOrNesting(INamedTypeSymbol typeSymbol)
        {
            for (INamedTypeSymbol? current = typeSymbol; current is not null; current = current.ContainingType)
            {
                if (current.IsGenericType)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Determines the type the validator validates and captures that type's accessible instance
        /// properties as value shapes. The validated type is the single type argument of the OrionGuard
        /// <c>IValidator&lt;T&gt;</c> the annotated type implements (directly, or through an
        /// <c>AbstractValidator&lt;T&gt;</c> / <c>FluentStyleValidator&lt;T&gt;</c> base, both of which
        /// surface <c>IValidator&lt;T&gt;</c>). When the annotated type does not implement the contract at
        /// all, the returned <c>found</c> flag is <c>false</c> and the caller skips it with a diagnostic
        /// rather than guessing a validated type from an unrelated annotated class.
        /// </summary>
        private static (string fullName, bool found, ImmutableArray<MemberShape> members) ResolveValidatedType(
            INamedTypeSymbol typeSymbol)
        {
            INamedTypeSymbol? validated = FindValidatedTypeArgument(typeSymbol);

            if (validated is null)
            {
                // The annotated type is not an OrionGuard validator: do not invent a validated type.
                return (string.Empty, false, ImmutableArray<MemberShape>.Empty);
            }

            string fullName = validated.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var members = CaptureMembers(validated);
            return (fullName, true, members);
        }

        /// <summary>
        /// Finds <c>T</c> from the OrionGuard <c>IValidator&lt;T&gt;</c> the type implements. Matching on the
        /// interface's full metadata name (not just a name ending in "Validator") keeps an unrelated
        /// annotated type that merely has a "...Validator" base from being mistaken for a participant in the
        /// contract.
        /// </summary>
        private static INamedTypeSymbol? FindValidatedTypeArgument(INamedTypeSymbol typeSymbol)
        {
            foreach (var iface in typeSymbol.AllInterfaces)
            {
                if (IsOrionGuardValidatorInterface(iface)
                    && iface.TypeArguments.Length == 1
                    && iface.TypeArguments[0] is INamedTypeSymbol ifaceArg)
                {
                    return ifaceArg;
                }
            }

            return null;
        }

        /// <summary>
        /// True when <paramref name="iface"/> is the OrionGuard <c>IValidator&lt;T&gt;</c> contract,
        /// identified by its full namespace and name so a same-named interface from another library does
        /// not match.
        /// </summary>
        private static bool IsOrionGuardValidatorInterface(INamedTypeSymbol iface)
        {
            return iface is { Name: "IValidator", IsGenericType: true }
                && iface.ContainingNamespace.ToDisplayString() == "Moongazing.OrionGuard.DependencyInjection";
        }

        /// <summary>
        /// Maps a symbol's <see cref="Accessibility"/> to the C# modifier keyword(s) the generated partial
        /// must repeat so its accessibility matches the user's partial. Defaults to <c>internal</c> for the
        /// not-applicable case, the C# default for a namespace-scoped type.
        /// </summary>
        private static string AccessibilityKeyword(Accessibility accessibility)
        {
            switch (accessibility)
            {
                case Accessibility.Public:
                    return "public";
                case Accessibility.Internal:
                    return "internal";
                case Accessibility.Protected:
                    return "protected";
                case Accessibility.ProtectedOrInternal:
                    return "protected internal";
                case Accessibility.ProtectedAndInternal:
                    return "private protected";
                case Accessibility.Private:
                    return "private";
                default:
                    return "internal";
            }
        }

        private static ImmutableArray<MemberShape> CaptureMembers(INamedTypeSymbol type)
        {
            var builder = ImmutableArray.CreateBuilder<MemberShape>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (INamedTypeSymbol? current = type; current is not null && current.SpecialType != SpecialType.System_Object;
                current = current.BaseType)
            {
                foreach (var member in current.GetMembers())
                {
                    if (member is not IPropertySymbol property)
                    {
                        continue;
                    }

                    if (property.DeclaredAccessibility != Accessibility.Public
                        || property.IsStatic
                        || property.IsWriteOnly
                        || property.GetMethod is null)
                    {
                        continue;
                    }

                    if (!seen.Add(property.Name))
                    {
                        continue;
                    }

                    var (category, numericKind, nullable) = Categorize(property.Type);
                    builder.Add(new MemberShape(property.Name, category, numericKind, nullable));
                }
            }

            return builder.ToImmutable();
        }

        private static (MemberTypeCategory category, NumericKind numericKind, bool nullable) Categorize(ITypeSymbol type)
        {
            bool nullable = type.IsReferenceType
                || type.NullableAnnotation == NullableAnnotation.Annotated
                || type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;

            // Unwrap Nullable<T> to inspect the underlying type.
            ITypeSymbol underlying = type;
            if (type is INamedTypeSymbol named
                && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
                && named.TypeArguments.Length == 1)
            {
                underlying = named.TypeArguments[0];
            }

            if (underlying.SpecialType == SpecialType.System_String)
            {
                return (MemberTypeCategory.String, NumericKind.None, nullable);
            }

            if (underlying.SpecialType == SpecialType.System_Boolean)
            {
                return (MemberTypeCategory.Boolean, NumericKind.None, nullable);
            }

            var numericKind = ClassifyNumeric(underlying.SpecialType);
            if (numericKind != NumericKind.None)
            {
                return (MemberTypeCategory.Numeric, numericKind, nullable);
            }

            // A non-string IEnumerable is a collection.
            if (IsEnumerable(underlying))
            {
                return (MemberTypeCategory.Collection, NumericKind.None, nullable);
            }

            if (underlying.IsReferenceType)
            {
                return (MemberTypeCategory.Object, NumericKind.None, nullable);
            }

            return (MemberTypeCategory.Other, NumericKind.None, nullable);
        }

        /// <summary>
        /// Maps a CLR numeric <see cref="SpecialType"/> to the <see cref="NumericKind"/> the emitter uses
        /// to render compiling bound literals and comparisons; returns <see cref="NumericKind.None"/> for
        /// a non-numeric type.
        /// </summary>
        private static NumericKind ClassifyNumeric(SpecialType specialType)
        {
            switch (specialType)
            {
                case SpecialType.System_SByte:
                case SpecialType.System_Int16:
                case SpecialType.System_Int32:
                case SpecialType.System_Int64:
                    return NumericKind.SignedIntegral;

                case SpecialType.System_Byte:
                case SpecialType.System_UInt16:
                case SpecialType.System_UInt32:
                case SpecialType.System_UInt64:
                    return NumericKind.UnsignedIntegral;

                case SpecialType.System_Single:
                    return NumericKind.Single;

                case SpecialType.System_Double:
                    return NumericKind.Double;

                case SpecialType.System_Decimal:
                    return NumericKind.Decimal;

                default:
                    return NumericKind.None;
            }
        }

        private static bool IsEnumerable(ITypeSymbol type)
        {
            if (type.SpecialType == SpecialType.System_String)
            {
                return false;
            }

            if (type.TypeKind == TypeKind.Array)
            {
                return true;
            }

            if (type is INamedTypeSymbol named && named.SpecialType == SpecialType.System_Collections_IEnumerable)
            {
                return true;
            }

            foreach (var iface in type.AllInterfaces)
            {
                if (iface.SpecialType == SpecialType.System_Collections_IEnumerable
                    || (iface.IsGenericType && iface.ConstructedFrom.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T))
                {
                    return true;
                }
            }

            return false;
        }

        private static void Execute(
            SourceProductionContext spc,
            OpenApiValidatorTarget target,
            ImmutableArray<OpenApiDocument> documents)
        {
            // Partiality contract. The emitter reopens the target and (for a nested target) every enclosing
            // type as a nested `partial` declaration; reopening a non-partial type with a partial is a
            // consumer compile error, so every link in the chain must be partial.
            //
            // For a top-level target the only link is the target itself: a non-partial target is reported
            // with OG1005 (the long-standing "add partial" guidance). For a nested target the whole chain is
            // governed by the nested-partiality contract OG1011, which names the first non-partial type
            // (outermost enclosing type first, then the target) so the consumer knows exactly which
            // declaration to fix. Either way no code is emitted for a non-partial chain.
            if (target.EnclosingTypes.Length == 0)
            {
                if (!target.IsPartial)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(
                        OpenApiDiagnostics.TargetNotPartial, target.GetLocation(), target.ClassName));
                    return;
                }
            }
            else if (TryFindNonPartialLink(target, out string offendingType))
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    OpenApiDiagnostics.NestedTargetNotPartial, target.GetLocation(), offendingType));
                return;
            }

            // A generic target (or one nested inside a generic type) cannot have its partial reconstructed
            // with the right type parameters and constraints yet; diagnose and skip rather than emit a
            // partial that would not compile. Documented as a known limitation in the package README/ROADMAP.
            if (target.IsGeneric)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    OpenApiDiagnostics.GenericTargetUnsupported, target.GetLocation(), target.ClassName));
                return;
            }

            // The validated type T must come from the OrionGuard IValidator<T> contract. When the
            // annotated type does not participate in that contract there is no T to emit against, so
            // diagnose and skip rather than emit IValidator<> with an empty type argument (uncompilable).
            if (!target.HasValidatedType)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    OpenApiDiagnostics.MissingValidatorContract, target.GetLocation(), target.ClassName));
                return;
            }

            // Locate the named document among the AdditionalFiles. A relative/sub-path in the attribute is
            // matched by path suffix first; a bare file name falls back to a basename match. An ambiguous
            // match (the name fits more than one file) is diagnosed rather than silently resolved.
            var documentMatch = FindDocument(documents, target.DocumentPath);
            if (documentMatch.IsAmbiguous)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    OpenApiDiagnostics.AmbiguousDocument,
                    target.GetLocation(), target.DocumentPath, documentMatch.AmbiguousPaths));
                return;
            }

            OpenApiDocument? match = documentMatch.Document;
            if (match is null || match.Value.Content is null)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    OpenApiDiagnostics.MissingDocument, target.GetLocation(), target.DocumentPath));
                return;
            }

            // Parse the document with the bundled JSON parser.
            JsonValue root;
            try
            {
                root = JsonParser.Parse(match.Value.Content);
            }
            catch (JsonParseException ex)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    OpenApiDiagnostics.InvalidDocument, target.GetLocation(), target.DocumentPath, ex.Message));
                return;
            }

            var resolver = new SchemaResolver(root);

            // Resolve the pointer to the root schema node.
            var pointerResult = resolver.ResolvePointer(target.SchemaPointer);
            if (!pointerResult.Success)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    OpenApiDiagnostics.UnresolvablePointer,
                    target.GetLocation(), target.SchemaPointer, target.DocumentPath, pointerResult.Error));
                return;
            }

            var rootSchema = OpenApiSchema.Parse(pointerResult.Node!);

            // Follow a top-level $ref if the pointer landed on one.
            var (resolvedRoot, rootRefError) = resolver.ResolveSchema(rootSchema);
            if (resolvedRoot is null)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    OpenApiDiagnostics.UnresolvableRef, target.GetLocation(), target.SchemaPointer, rootRefError));
                return;
            }

            if (resolvedRoot.HasUnsupportedConstruct)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    OpenApiDiagnostics.UnsupportedConstruct,
                    target.GetLocation(), target.SchemaPointer, resolvedRoot.UnsupportedConstructName));
                // Continue: enforce whatever else the schema declares.
            }

            ReportSchemaIssues(spc, target, target.SchemaPointer, resolvedRoot);

            // Bind each schema property to a member on the validated type and resolve property-level $refs.
            var bindings = BuildBindings(spc, target, resolvedRoot, resolver);

            var model = new ValidatorEmitModel(
                target.Namespace, target.ClassName, target.Accessibility, target.ValidatedTypeFullName,
                target.EnclosingTypes, bindings);

            string source = ValidatorEmitter.Emit(model);
            spc.AddSource(BuildHintName(target), SourceText.From(source, Encoding.UTF8));
        }

        private static IReadOnlyList<PropertyBinding> BuildBindings(
            SourceProductionContext spc,
            OpenApiValidatorTarget target,
            OpenApiSchema rootSchema,
            SchemaResolver resolver)
        {
            var bindings = new List<PropertyBinding>();
            var memberLookup = target.Members.ToDictionary(m => m.Name, m => m, IgnoreCaseComparer.Instance);

            foreach (var property in rootSchema.Properties)
            {
                // Resolve a property-level $ref so the property's constraints come from the referenced schema.
                var (resolvedSchema, refError) = resolver.ResolveSchema(property.Schema);
                if (resolvedSchema is null)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(
                        OpenApiDiagnostics.UnresolvableRef, target.GetLocation(), property.Name, refError));
                    continue;
                }

                if (resolvedSchema.HasUnsupportedConstruct)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(
                        OpenApiDiagnostics.UnsupportedConstruct,
                        target.GetLocation(), property.Name, resolvedSchema.UnsupportedConstructName));
                    // Fall through: emit the supported constraints on this property.
                }

                ReportSchemaIssues(spc, target, property.Name, resolvedSchema);

                bool required = rootSchema.Required.Contains(property.Name);

                // Match the schema property to a C# member by name (case-insensitive, so firstName binds
                // to FirstName). A property the type does not expose is skipped: we cannot read it, and
                // emitting a reference to a missing member would break the consumer build.
                if (!TryMatchMember(memberLookup, property.Name, out var member))
                {
                    continue;
                }

                bindings.Add(new PropertyBinding(
                    property.Name,
                    member.Name,
                    member.Category,
                    member.NumericKind,
                    member.IsReferenceTypeOrNullable,
                    resolvedSchema,
                    required));
            }

            return bindings;
        }

        private static bool TryMatchMember(
            Dictionary<string, MemberShape> lookup, string schemaName, out MemberShape member)
        {
            if (lookup.TryGetValue(schemaName, out member))
            {
                return true;
            }

            // Also try a PascalCase form (firstName -> FirstName), since C# properties are conventionally
            // PascalCase while OpenAPI property names are often camelCase.
            if (schemaName.Length > 0)
            {
                string pascal = char.ToUpperInvariant(schemaName[0]) + schemaName.Substring(1);
                if (lookup.TryGetValue(pascal, out member))
                {
                    return true;
                }
            }

            member = default;
            return false;
        }

        /// <summary>
        /// Raises OG1007 for each keyword the parser rejected on <paramref name="schema"/> (for example an
        /// integer keyword whose value overflowed <see cref="int"/>). The constraint was skipped during
        /// parse; this makes the skip visible instead of silent.
        /// </summary>
        private static void ReportSchemaIssues(
            SourceProductionContext spc, OpenApiValidatorTarget target, string context, OpenApiSchema schema)
        {
            foreach (var issue in schema.Issues)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    OpenApiDiagnostics.InvalidKeywordValue,
                    target.GetLocation(), issue.Keyword, context, issue.RawValue, issue.Reason));
            }
        }

        /// <summary>
        /// Builds the generated-source hint name from the target's full declaring-type path: the namespace,
        /// every enclosing type name, and the leaf class name. Keying on the whole path (not just namespace +
        /// leaf) keeps two same-leaf validators nested in different outer types in the same namespace
        /// (for example <c>Outer1.InnerValidator</c> and <c>Outer2.InnerValidator</c>) from colliding, since
        /// Roslyn requires every hint name in a generator to be unique. Dots are filename-safe in a hint
        /// name; any other character is replaced so the name stays well-formed.
        /// </summary>
        private static string BuildHintName(OpenApiValidatorTarget target)
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(target.Namespace))
            {
                parts.Add(target.Namespace!);
            }

            foreach (var enclosing in target.EnclosingTypes)
            {
                parts.Add(enclosing.Name);
            }

            parts.Add(target.ClassName);

            string qualified = string.Join(".", parts);

            var sb = new StringBuilder(qualified.Length);
            foreach (char c in qualified)
            {
                sb.Append(c == '.' || char.IsLetterOrDigit(c) ? c : '_');
            }

            return sb.Append(".OpenApiValidator.g.cs").ToString();
        }

        /// <summary>
        /// The outcome of resolving the <c>[OpenApiValidator]</c> document name against the AdditionalFiles:
        /// either a single matched document, no match, or an ambiguous match (the name fit more than one
        /// file). Ambiguity is reported rather than silently resolved so a build does not bind to an
        /// arbitrary file.
        /// </summary>
        private readonly struct DocumentMatch
        {
            private DocumentMatch(OpenApiDocument? document, bool isAmbiguous, string ambiguousPaths)
            {
                Document = document;
                IsAmbiguous = isAmbiguous;
                AmbiguousPaths = ambiguousPaths;
            }

            public OpenApiDocument? Document { get; }

            public bool IsAmbiguous { get; }

            /// <summary>A comma-separated list of the candidate paths when <see cref="IsAmbiguous"/>.</summary>
            public string AmbiguousPaths { get; }

            public static DocumentMatch Matched(OpenApiDocument document) =>
                new DocumentMatch(document, isAmbiguous: false, string.Empty);

            public static readonly DocumentMatch None =
                new DocumentMatch(null, isAmbiguous: false, string.Empty);

            public static DocumentMatch Ambiguous(IEnumerable<OpenApiDocument> candidates) =>
                new DocumentMatch(
                    null,
                    isAmbiguous: true,
                    string.Join(", ", candidates.Select(c => c.FullPath)));
        }

        /// <summary>
        /// Resolves the document named by <c>[OpenApiValidator]</c> among the AdditionalFiles. The
        /// attribute's value is honored as a relative/sub-path first (matched by full-path suffix on a
        /// segment boundary), so <c>schemas/openapi.json</c> binds to the file under <c>schemas</c> even
        /// when another <c>openapi.json</c> exists elsewhere. A bare file name falls back to a basename
        /// match. Either way, a name that fits more than one file is reported as ambiguous instead of
        /// silently picking one.
        /// </summary>
        private static DocumentMatch FindDocument(ImmutableArray<OpenApiDocument> documents, string documentPath)
        {
            string normalizedTarget = documentPath.Replace('\\', '/').TrimStart('/');
            bool targetHasDirectory = normalizedTarget.IndexOf('/') >= 0;

            // First honor the relative/sub-path the attribute specifies: match on a full-path suffix that
            // begins at a path-segment boundary, so "schemas/openapi.json" matches ".../schemas/openapi.json"
            // but not ".../other-schemas/openapi.json".
            var suffixMatches = new List<OpenApiDocument>();
            foreach (var doc in documents)
            {
                string docPath = doc.FullPath.Replace('\\', '/');
                if (IsPathSuffix(docPath, normalizedTarget))
                {
                    suffixMatches.Add(doc);
                }
            }

            if (suffixMatches.Count == 1)
            {
                return DocumentMatch.Matched(suffixMatches[0]);
            }

            if (suffixMatches.Count > 1)
            {
                return DocumentMatch.Ambiguous(suffixMatches);
            }

            // The attribute named a path that no file's suffix satisfied. When it carried a directory the
            // path was explicit and simply did not resolve; do not fall back to a looser basename match.
            if (targetHasDirectory)
            {
                return DocumentMatch.None;
            }

            // Bare file name: fall back to a basename match, reporting ambiguity when more than one file
            // shares the name.
            string targetName = System.IO.Path.GetFileName(documentPath);
            var nameMatches = new List<OpenApiDocument>();
            foreach (var doc in documents)
            {
                if (string.Equals(doc.FileName, targetName, StringComparison.OrdinalIgnoreCase))
                {
                    nameMatches.Add(doc);
                }
            }

            if (nameMatches.Count == 1)
            {
                return DocumentMatch.Matched(nameMatches[0]);
            }

            if (nameMatches.Count > 1)
            {
                return DocumentMatch.Ambiguous(nameMatches);
            }

            return DocumentMatch.None;
        }

        /// <summary>
        /// True when <paramref name="candidate"/> matches <paramref name="suffix"/> at a path-segment
        /// boundary: the candidate equals the suffix, or ends with <c>/</c> + suffix. Both are forward-slash
        /// normalized by the caller.
        /// </summary>
        private static bool IsPathSuffix(string candidate, string suffix)
        {
            if (string.Equals(candidate, suffix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return candidate.Length > suffix.Length
                && candidate[candidate.Length - suffix.Length - 1] == '/'
                && candidate.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
        }

        private sealed class IgnoreCaseComparer : IEqualityComparer<string>
        {
            public static readonly IgnoreCaseComparer Instance = new IgnoreCaseComparer();

            public bool Equals(string x, string y) => string.Equals(x, y, StringComparison.OrdinalIgnoreCase);

            public int GetHashCode(string obj) => obj.ToLowerInvariant().GetHashCode();
        }
    }
}
