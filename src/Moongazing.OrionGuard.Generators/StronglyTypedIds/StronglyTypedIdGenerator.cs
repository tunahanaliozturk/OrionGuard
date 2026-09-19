#nullable enable

using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Moongazing.OrionGuard.Generators.StronglyTypedIds
{
    /// <summary>
    /// Incremental source generator for <c>[StronglyTypedId&lt;TValue&gt;]</c>-decorated readonly
    /// partial structs. Emits the type body plus EF Core / System.Text.Json / TypeConverter
    /// companions in subsequent generator passes.
    /// </summary>
    [Generator(LanguageNames.CSharp)]
    public sealed class StronglyTypedIdGenerator : IIncrementalGenerator
    {

        private const string TypeConverterAttributeFullName = "System.ComponentModel.TypeConverterAttribute";

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            context.RegisterPostInitializationOutput(ctx =>
                ctx.AddSource(
                    "StronglyTypedIdAttribute.g.cs",
                    SourceText.From(StronglyTypedIdAttributeSource.Source, Encoding.UTF8)));

            var targets = context.SyntaxProvider
                .ForAttributeWithMetadataName(
                    StronglyTypedIdAttributeSource.FullName + "`1",
                    predicate: static (node, _) => node is StructDeclarationSyntax sds
                        && sds.Modifiers.Any(m => m.ValueText == "partial")
                        && sds.Modifiers.Any(m => m.ValueText == "readonly"),
                    transform: static (ctx, _) => Transform(ctx))
                .Where(static t => t is not null);

            var hasEfCore = context.CompilationProvider
                .Select(static (compilation, _) =>
                    compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter") is not null);

            context.RegisterSourceOutput(targets.Combine(hasEfCore), static (spc, pair) =>
            {
                var target = pair.Left;
                var efCoreAvailable = pair.Right;
                if (target is null) return;

                spc.AddSource(
                    target.HintName + ".StronglyTypedId.g.cs",
                    SourceText.From(
                        StronglyTypedIdEmitter.EmitPartial(
                            target.Namespace, target.TypeName, target.ValueType,
                            emitTypeConverterAttribute: !target.HasTypeConverterAttribute),
                        Encoding.UTF8));

                if (efCoreAvailable)
                {
                    spc.AddSource(
                        EfCoreConverterEmitter.HintName(target.HintName),
                        SourceText.From(
                            EfCoreConverterEmitter.Emit(target.Namespace, target.TypeName, target.ValueType),
                            Encoding.UTF8));
                }

                spc.AddSource(
                    JsonConverterEmitter.HintName(target.HintName),
                    SourceText.From(
                        JsonConverterEmitter.Emit(target.Namespace, target.TypeName, target.ValueType),
                        Encoding.UTF8));

                spc.AddSource(
                    TypeConverterEmitter.HintName(target.HintName),
                    SourceText.From(
                        TypeConverterEmitter.Emit(target.Namespace, target.TypeName, target.ValueType),
                        Encoding.UTF8));

                spc.AddSource(
                    ParsableEmitter.HintName(target.HintName),
                    SourceText.From(
                        ParsableEmitter.Emit(target.Namespace, target.TypeName, target.ValueType),
                        Encoding.UTF8));
            });
        }

        private static StronglyTypedIdTarget? Transform(GeneratorAttributeSyntaxContext ctx)
        {
            if (ctx.TargetSymbol is not INamedTypeSymbol symbol) return null;

            var attribute = ctx.Attributes.FirstOrDefault();
            if (attribute is null || attribute.AttributeClass is null) return null;

            var typeArg = attribute.AttributeClass.TypeArguments.FirstOrDefault();
            if (typeArg is null) return null;

            var fqName = typeArg.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                .Replace("global::", string.Empty);

            if (!SupportedValueTypeMap.TryParse(fqName, out var mapped)) return null;

            // A user who already wired a converter by hand keeps it: emitting a second
            // [JsonConverter] or [TypeConverter] would fail the build with CS0579.
            var attributeNames = symbol.GetAttributes()
                .Select(a => a.AttributeClass?.ToDisplayString())
                .ToArray();

            return new StronglyTypedIdTarget(
                symbol.ContainingNamespace.IsGlobalNamespace ? null : symbol.ContainingNamespace.ToDisplayString(),
                symbol.Name,
                HintNames.ForType(symbol),
                mapped,
                attributeNames.Contains(TypeConverterAttributeFullName));
        }

        /// <summary>
        /// Value-equal (a record over strings, bools and an enum), so an unrelated edit leaves the
        /// generated sources cached instead of regenerating them.
        /// </summary>
        private sealed record StronglyTypedIdTarget
        {
            public StronglyTypedIdTarget(
                string? @namespace,
                string typeName,
                string hintName,
                SupportedValueType valueType,
                bool hasTypeConverterAttribute)
            {
                Namespace = @namespace;
                TypeName = typeName;
                HintName = hintName;
                ValueType = valueType;
                HasTypeConverterAttribute = hasTypeConverterAttribute;
            }

            /// <summary>The containing namespace, or <c>null</c> for the global namespace.</summary>
            public string? Namespace { get; }
            public string TypeName { get; }
            public string HintName { get; }
            public SupportedValueType ValueType { get; }
            public bool HasTypeConverterAttribute { get; }
        }
    }
}
