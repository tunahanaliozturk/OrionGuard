using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace Moongazing.OrionGuard.Core;

/// <summary>
/// Process-wide cache of compiled property accessors, shared by <see cref="ObjectValidator{T}"/> and
/// <see cref="Delta{T}"/>. The generic static class gives every <c>(T, TProperty)</c> pair its own cache.
/// </summary>
/// <remarks>
/// <para>
/// A compiled accessor may be shared only between selectors that read exactly the same members, so the
/// key must identify the whole selector. The previous keys did not: the last member's name made
/// <c>o =&gt; o.Customer.Name</c> and <c>o =&gt; o.Name</c> share one accessor, and
/// <see cref="Expression.ToString()"/> prints two closures over different captured variables identically.
/// </para>
/// <para>
/// Only two selector shapes are cached: <c>o =&gt; o.Member</c>, keyed by its <see cref="MemberInfo"/> (no
/// allocation on a hit), and pure member chains such as <c>o =&gt; o.Items.Count</c>, keyed by the full
/// member path, in both cases through a widening conversion if the selector reads the member at a wider
/// type. Anything else (narrowing or numeric casts, method calls, indexers, captured variables) is compiled
/// on each call, because nothing short of the full expression tree identifies it.
/// </para>
/// </remarks>
internal static class AccessorCache<T, TProperty>
{
    private static readonly ConcurrentDictionary<MemberInfo, Func<T, TProperty>> ByMember = new();
    private static readonly ConcurrentDictionary<MemberPath, Func<T, TProperty>> ByPath = new();

    internal static Func<T, TProperty> Get(Expression<Func<T, TProperty>> selector)
    {
        if (Unwrap(selector.Body) is MemberExpression member)
        {
            var parameter = selector.Parameters[0];
            if (member.Expression == parameter)
            {
                return ByMember.GetOrAdd(member.Member, static (_, compile) => compile.Compile(), selector);
            }

            if (MemberPath.TryCreate(member, parameter, out var path))
            {
                return ByPath.GetOrAdd(path, static (_, compile) => compile.Compile(), selector);
            }
        }

        return selector.Compile();
    }

    // A selector read at a wider type than the member is declared -- o => o.Items, a List<TItem> taken as
    // IEnumerable<TItem> -- arrives wrapped in a conversion the cache would otherwise refuse to key on. The
    // conversion target is TProperty, already part of the key, so the member alone still identifies the
    // accessor. Only a widening conversion qualifies: a downcast differs between (T)x and x as T, and a
    // numeric or user-defined conversion carries semantics -- overflow checking, an operator -- that the
    // member on its own does not capture.
    private static Expression Unwrap(Expression body) =>
        body is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.TypeAs, Method: null } cast
        && cast.Operand.Type.IsAssignableTo(cast.Type)
            ? cast.Operand
            : body;
}

/// <summary>
/// The members read by a selector of the form <c>o =&gt; o.A.B.C</c>, innermost first. Compared member
/// by member, so two chains are equal only when they read the same members in the same order.
/// </summary>
internal readonly record struct MemberPath(MemberInfo[] Members)
{
    /// <summary>
    /// Succeeds when <paramref name="member"/> is a chain of member accesses that starts at
    /// <paramref name="parameter"/>; fails for chains that start at a captured variable or a static member.
    /// </summary>
    internal static bool TryCreate(MemberExpression member, ParameterExpression parameter, out MemberPath path)
    {
        var members = new List<MemberInfo>();
        Expression? current = member;
        while (current is MemberExpression link)
        {
            members.Add(link.Member);
            current = link.Expression;
        }

        path = new MemberPath(members.ToArray());
        return current == parameter;
    }

    public bool Equals(MemberPath other) => Members.AsSpan().SequenceEqual(other.Members);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var member in Members)
        {
            hash.Add(member);
        }
        return hash.ToHashCode();
    }
}
