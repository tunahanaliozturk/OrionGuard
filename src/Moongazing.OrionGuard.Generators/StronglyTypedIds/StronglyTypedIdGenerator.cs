#nullable enable

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Linq;
using System.Text;

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

            context.RegisterSourceOutput(targets, static (spc, target) =>
            {
                if (target is null) return;

                spc.AddSource(
                    $"{target.TypeName}.StronglyTypedId.g.cs",
                    SourceText.From(
                        StronglyTypedIdEmitter.EmitPartial(target.Namespace, target.TypeName, target.ValueType),
                        Encoding.UTF8));

                spc.AddSource(
                    EfCoreConverterEmitter.HintName(target.TypeName),
                    SourceText.From(
                        EfCoreConverterEmitter.Emit(target.Namespace, target.TypeName, target.ValueType),
                        Encoding.UTF8));

                spc.AddSource(
                    JsonConverterEmitter.HintName(target.TypeName),
                    SourceText.From(
                        JsonConverterEmitter.Emit(target.Namespace, target.TypeName, target.ValueType),
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

            return new StronglyTypedIdTarget(
                symbol.ContainingNamespace.ToDisplayString(),
                symbol.Name,
                mapped);
        }

        private sealed class StronglyTypedIdTarget
        {
            public StronglyTypedIdTarget(string @namespace, string typeName, SupportedValueType valueType)
            {
                Namespace = @namespace;
                TypeName = typeName;
                ValueType = valueType;
            }

            public string Namespace { get; }
            public string TypeName { get; }
            public SupportedValueType ValueType { get; }
        }
    }
}
