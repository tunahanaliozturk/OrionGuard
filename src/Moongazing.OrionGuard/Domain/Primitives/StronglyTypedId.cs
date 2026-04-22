using System;

namespace Moongazing.OrionGuard.Domain.Primitives;

/// <summary>
/// Base record for strongly-typed identifiers — avoids primitive obsession when modeling
/// domain identities. Equality and <c>GetHashCode</c> are provided by the <see langword="record"/>
/// compiler synthesis; derived types contribute type identity so that <c>OrderId(guid)</c> and
/// <c>CustomerId(guid)</c> with the same <see cref="Value"/> are <b>not</b> equal.
/// </summary>
/// <typeparam name="TValue">The underlying primitive type (typically <see cref="Guid"/>,
/// <see cref="int"/>, <see cref="long"/>, or <see cref="string"/>).</typeparam>
/// <param name="Value">The underlying primitive value wrapped by this strongly-typed identifier.</param>
public abstract record StronglyTypedId<TValue>(TValue Value) : IStronglyTypedId<TValue>
    where TValue : notnull, IEquatable<TValue>;
