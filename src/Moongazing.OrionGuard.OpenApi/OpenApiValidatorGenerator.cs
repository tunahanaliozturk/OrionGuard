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

            bool isPartial = context.TargetNode is ClassDeclarationSyntax cds
                && cds.Modifiers.Any(m => m.ValueText == "partial");

            string? namespaceName = typeSymbol.ContainingNamespace.IsGlobalNamespace
                ? null
                : typeSymbol.ContainingNamespace.ToDisplayString();

            // Infer the validated type T. Prefer an explicit IValidator<T>/AbstractValidator<T>/
            // FluentStyleValidator<T> base or interface; fall back to the only candidate the document
            // names. If none is found, validate the annotated type's own members (it is a DTO itself).
            (string validatedTypeFullName, bool hasValidatedType, ImmutableArray<MemberShape> members) =
                ResolveValidatedType(typeSymbol);

            var location = typeSymbol.Locations.FirstOrDefault(l => l.IsInSource) ?? Location.None;
            var span = location.SourceSpan;
            string locationPath = location.SourceTree?.FilePath ?? string.Empty;

            return new OpenApiValidatorTarget(
                namespaceName,
                typeSymbol.Name,
                validatedTypeFullName,
                isPartial,
                hasValidatedType,
                documentPath,
                schemaPointer,
                members,
                locationPath,
                span.Start,
                span.Length);
        }

        /// <summary>
        /// Determines the type the validator validates and captures that type's accessible instance
        /// properties as value shapes. The validated type is the type argument of a validator base or
        /// interface when present, otherwise the annotated type itself.
        /// </summary>
        private static (string fullName, bool found, ImmutableArray<MemberShape> members) ResolveValidatedType(
            INamedTypeSymbol typeSymbol)
        {
            INamedTypeSymbol? validated = FindValidatedTypeArgument(typeSymbol);

            if (validated is null)
            {
                // No validator base/interface: treat the annotated class as the DTO under validation.
                validated = typeSymbol;
            }

            string fullName = validated.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var members = CaptureMembers(validated);
            return (fullName, true, members);
        }

        private static INamedTypeSymbol? FindValidatedTypeArgument(INamedTypeSymbol typeSymbol)
        {
            // Walk base types looking for a single-arg generic validator base
            // (AbstractValidator<T> / FluentStyleValidator<T> / anything ending in "Validator").
            for (INamedTypeSymbol? current = typeSymbol.BaseType; current is not null; current = current.BaseType)
            {
                if (current.IsGenericType && current.TypeArguments.Length == 1
                    && current.TypeArguments[0] is INamedTypeSymbol baseArg
                    && current.Name.EndsWith("Validator", StringComparison.Ordinal))
                {
                    return baseArg;
                }
            }

            // Otherwise look for IValidator<T>.
            foreach (var iface in typeSymbol.AllInterfaces)
            {
                if (iface.Name == "IValidator" && iface.TypeArguments.Length == 1
                    && iface.TypeArguments[0] is INamedTypeSymbol ifaceArg)
                {
                    return ifaceArg;
                }
            }

            return null;
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

                    var (category, nullable) = Categorize(property.Type);
                    builder.Add(new MemberShape(property.Name, category, nullable));
                }
            }

            return builder.ToImmutable();
        }

        private static (MemberTypeCategory category, bool nullable) Categorize(ITypeSymbol type)
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
                return (MemberTypeCategory.String, nullable);
            }

            if (underlying.SpecialType == SpecialType.System_Boolean)
            {
                return (MemberTypeCategory.Boolean, nullable);
            }

            if (IsNumeric(underlying.SpecialType))
            {
                return (MemberTypeCategory.Numeric, nullable);
            }

            // A non-string IEnumerable is a collection.
            if (IsEnumerable(underlying))
            {
                return (MemberTypeCategory.Collection, nullable);
            }

            if (underlying.IsReferenceType)
            {
                return (MemberTypeCategory.Object, nullable);
            }

            return (MemberTypeCategory.Other, nullable);
        }

        private static bool IsNumeric(SpecialType specialType)
        {
            switch (specialType)
            {
                case SpecialType.System_SByte:
                case SpecialType.System_Byte:
                case SpecialType.System_Int16:
                case SpecialType.System_UInt16:
                case SpecialType.System_Int32:
                case SpecialType.System_UInt32:
                case SpecialType.System_Int64:
                case SpecialType.System_UInt64:
                case SpecialType.System_Single:
                case SpecialType.System_Double:
                case SpecialType.System_Decimal:
                    return true;
                default:
                    return false;
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
            // A non-partial target cannot be extended; warn and skip.
            if (!target.IsPartial)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    OpenApiDiagnostics.TargetNotPartial, target.GetLocation(), target.ClassName));
                return;
            }

            // Locate the named document among the AdditionalFiles (match on file name or full path).
            OpenApiDocument? match = FindDocument(documents, target.DocumentPath);
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

            // Bind each schema property to a member on the validated type and resolve property-level $refs.
            var bindings = BuildBindings(spc, target, resolvedRoot, resolver);

            var model = new ValidatorEmitModel(
                target.Namespace, target.ClassName, target.ValidatedTypeFullName, bindings);

            string source = ValidatorEmitter.Emit(model);
            spc.AddSource($"{target.ClassName}.OpenApiValidator.g.cs", SourceText.From(source, Encoding.UTF8));
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

        private static OpenApiDocument? FindDocument(ImmutableArray<OpenApiDocument> documents, string documentPath)
        {
            string targetName = System.IO.Path.GetFileName(documentPath);

            // Prefer an exact file-name match; fall back to a full-path suffix match so a relative path
            // in the attribute (subdir/openapi.json) also resolves.
            foreach (var doc in documents)
            {
                if (string.Equals(doc.FileName, targetName, StringComparison.OrdinalIgnoreCase))
                {
                    return doc;
                }
            }

            string normalized = documentPath.Replace('\\', '/');
            foreach (var doc in documents)
            {
                string docPath = doc.FullPath.Replace('\\', '/');
                if (docPath.EndsWith(normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return doc;
                }
            }

            return null;
        }

        private sealed class IgnoreCaseComparer : IEqualityComparer<string>
        {
            public static readonly IgnoreCaseComparer Instance = new IgnoreCaseComparer();

            public bool Equals(string x, string y) => string.Equals(x, y, StringComparison.OrdinalIgnoreCase);

            public int GetHashCode(string obj) => obj.ToLowerInvariant().GetHashCode();
        }
    }
}
