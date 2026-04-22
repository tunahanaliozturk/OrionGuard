using System;

namespace Moongazing.OrionGuard.Domain.Primitives;

/// <summary>
/// Marker interface implemented by both the <see cref="StronglyTypedId{TValue}"/> abstract
/// record (manual-use style) and by source-generated readonly partial struct identifiers
/// produced by the <c>[StronglyTypedId&lt;TValue&gt;]</c> generator. Provides a single
/// abstraction that guard extensions and other consumers can target regardless of which
/// declaration style the user chose.
/// </summary>
/// <typeparam name="TValue">Underlying primitive type (e.g. <see cref="Guid"/>, <see cref="int"/>,
/// <see cref="long"/>, <see cref="string"/>).</typeparam>
public interface IStronglyTypedId<TValue>
    where TValue : notnull, IEquatable<TValue>
{
    /// <summary>Gets the wrapped primitive value.</summary>
    TValue Value { get; }
}
