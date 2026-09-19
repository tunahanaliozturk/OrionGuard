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
        /// every other character that is not a letter or digit gets an escape of its own, so two different
        /// metadata names can never produce the same stem: a type named <c>A_B</c> and a type <c>B</c>
        /// nested in <c>A</c> both used to become <c>A_B</c>.
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
                switch (c)
                {
                    case '.':
                        sb.Append(c);
                        break;
                    case '_':
                        sb.Append("__");   // so an underscore in a name never reads as an escape
                        break;
                    case '+':
                        sb.Append("_n");   // nesting
                        break;
                    case '`':
                        sb.Append("_g");   // generic arity
                        break;
                    default:
                        sb.Append(char.IsLetterOrDigit(c) ? c.ToString() : "_x");
                        break;
                }
            }

            return sb.ToString();
        }
    }
}
