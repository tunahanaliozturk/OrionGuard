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
                .CreateSyntaxProvider(
                    predicate: static (node, _) => node is StructDeclarationSyntax sds
                        && sds.Modifiers.Any(m => m.ValueText == "partial")
                        && sds.Modifiers.Any(m => m.ValueText == "readonly")
                        && HasStronglyTypedIdAttribute(sds),
                    transform: static (ctx, _) => TransformSyntax(ctx))
                .Where(static t => t is not null);

            context.RegisterSourceOutput(targets, static (spc, target) =>
            {
                if (target is null) return;

                var source = StronglyTypedIdEmitter.EmitPartial(
                    target.Namespace, target.TypeName, target.ValueType);

                spc.AddSource(
                    $"{target.TypeName}.StronglyTypedId.g.cs",
                    SourceText.From(source, Encoding.UTF8));
            });
        }

        private static bool HasStronglyTypedIdAttribute(StructDeclarationSyntax sds)
        {
            return sds.AttributeLists
                .SelectMany(al => al.Attributes)
                .Any(a =>
                {
                    var nameStr = a.Name.ToString();
                    return nameStr.Contains("StronglyTypedId");
                });
        }

        private static StronglyTypedIdTarget? TransformSyntax(GeneratorSyntaxContext ctx)
        {
            if (ctx.Node is not StructDeclarationSyntax sds) return null;

            var symbol = ctx.SemanticModel.GetDeclaredSymbol(sds) as INamedTypeSymbol;
            if (symbol is null) return null;

            var attribute = symbol.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.Name == "StronglyTypedIdAttribute");

            if (attribute?.AttributeClass is null) return null;

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
            public string Namespace { get; }
            public string TypeName { get; }
            public SupportedValueType ValueType { get; }

            public StronglyTypedIdTarget(string @namespace, string typeName, SupportedValueType valueType)
            {
                Namespace = @namespace;
                TypeName = typeName;
                ValueType = valueType;
            }
        }
    }
}
