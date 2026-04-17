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
    /// <typeparam name="TId">The strongly-typed identifier type.</typeparam>
    /// <param name="id">The strongly-typed identifier to validate.</param>
    /// <param name="parameterName">The parameter name (for error messages).</param>
    /// <returns>The validated <paramref name="id"/> for chaining.</returns>
    /// <exception cref="NullValueException">When <paramref name="id"/> is <see langword="null"/>.</exception>
    /// <exception cref="ZeroValueException">When the wrapped value is default.</exception>
    public static TId AgainstDefaultStronglyTypedId<TId>(
        this TId id,
        [CallerArgumentExpression(nameof(id))] string? parameterName = null)
        where TId : class
    {
        if (id is null)
        {
            throw new NullValueException(parameterName ?? nameof(id));
        }

        // Use reflection to check if this is a StronglyTypedId derivative
        var idType = id.GetType();
        var baseType = idType.BaseType;

        if (baseType != null && baseType.IsGenericType)
        {
            var genericDef = baseType.GetGenericTypeDefinition();

            // Check if it inherits from StronglyTypedId<T>
            if (genericDef == typeof(StronglyTypedId<>))
            {
                var valueType = baseType.GetGenericArguments()[0];
                var valueProperty = idType.GetProperty("Value",
                    System.Reflection.BindingFlags.IgnoreCase |
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.Instance);

                if (valueProperty != null)
                {
                    var value = valueProperty.GetValue(id);

                    // Check if value is default
                    bool isDefault = IsDefaultValue(value, valueType);

                    if (isDefault)
                    {
                        throw new ZeroValueException(parameterName ?? nameof(id));
                    }
                }
            }
        }

        return id;
    }

    private static bool IsDefaultValue(object? value, Type valueType)
    {
        // Null check
        if (value is null)
        {
            return true;
        }

        // String special case
        if (valueType == typeof(string))
        {
            return string.IsNullOrEmpty((string)value);
        }

        // Value type default check
        if (valueType.IsValueType)
        {
            var defaultValue = Activator.CreateInstance(valueType);
            return value.Equals(defaultValue);
        }

        return false;
    }
}
