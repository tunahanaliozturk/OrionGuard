#nullable enable

using System.Text;
using Microsoft.CodeAnalysis;

namespace Moongazing.OrionGuard.Generators
{
    internal static class HintNames
    {
        /// <summary>
        /// Returns a hint-name stem built from the type's full metadata name: namespace, every enclosing
        /// type, and the type itself with its generic arity (for example <c>Orders.Outer_Inner</c>).
        /// Roslyn requires every hint name of a generator to be unique and, when two sources collide,
        /// fails the whole generator run, dropping every generated file. A stem built from the simple name
        /// alone collided as soon as two namespaces declared a type with the same name. Dots are kept;
        /// any other character that is not a letter or digit becomes an underscore.
        /// </summary>
        public static string ForType(INamedTypeSymbol type)
        {
            string name = type.MetadataName;
            for (INamedTypeSymbol? containing = type.ContainingType; containing is not null; containing = containing.ContainingType)
            {
                name = containing.MetadataName + "+" + name;
            }

            if (!type.ContainingNamespace.IsGlobalNamespace)
            {
                name = type.ContainingNamespace.ToDisplayString() + "." + name;
            }

            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                sb.Append(c == '.' || char.IsLetterOrDigit(c) ? c : '_');
            }

            return sb.ToString();
        }
    }
}
