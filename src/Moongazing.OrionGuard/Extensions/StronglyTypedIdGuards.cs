using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Moongazing.OrionGuard.Domain.Primitives;
using Moongazing.OrionGuard.Exceptions;

namespace Moongazing.OrionGuard.Extensions;

/// <summary>
/// Guard extensions for <see cref="StronglyTypedId{TValue}"/>-derived types.
/// </summary>
public static class StronglyTypedIdGuards
{
    /// <summary>
    /// Throws when <paramref name="id"/> is null or its wrapped value equals the default of its underlying type
    /// (<see cref="Guid.Empty"/>, <c>0</c>, <c>""</c>).
    /// </summary>
    /// <typeparam name="TValue">Underlying primitive type of the strongly-typed identifier.</typeparam>
    /// <param name="id">The strongly-typed identifier to validate.</param>
    /// <param name="parameterName">The parameter name (for error messages).</param>
    /// <returns>The validated <paramref name="id"/> for chaining.</returns>
    /// <exception cref="NullValueException">When <paramref name="id"/> is <see langword="null"/>.</exception>
    /// <exception cref="ZeroValueException">When the wrapped value is the default of <typeparamref name="TValue"/>
    /// (including empty string).</exception>
    public static StronglyTypedId<TValue> AgainstDefaultStronglyTypedId<TValue>(
        this StronglyTypedId<TValue> id,
        [CallerArgumentExpression(nameof(id))] string? parameterName = null)
        where TValue : notnull, IEquatable<TValue>
    {
        if (id is null)
        {
            throw new NullValueException(parameterName ?? nameof(id));
        }

        if (EqualityComparer<TValue>.Default.Equals(id.Value, default!) ||
            (id.Value is string s && string.IsNullOrEmpty(s)))
        {
            throw new ZeroValueException(parameterName ?? nameof(id));
        }

        return id;
    }
}
