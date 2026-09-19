using System.Globalization;

namespace Moongazing.OrionGuard.Core;

/// <summary>
/// Compares a validated value with a threshold by numeric value, even when the two are of different
/// built-in numeric types.
/// </summary>
/// <remarks>
/// <para>
/// Comparison guards such as <c>GreaterThan&lt;TValue&gt;(TValue min)</c> infer <c>TValue</c> from the
/// threshold, so <c>GreaterThan(0)</c> is <c>GreaterThan&lt;int&gt;</c> even when the value is a
/// <see cref="decimal"/>, <see cref="long"/> or <see cref="double"/>. Type-testing the value against
/// <c>TValue</c> therefore failed and the rule was skipped. Every comparison guard routes through this
/// helper instead.
/// </para>
/// <para>
/// Supported numeric types: <see cref="sbyte"/>, <see cref="byte"/>, <see cref="short"/>,
/// <see cref="ushort"/>, <see cref="int"/>, <see cref="uint"/>, <see cref="long"/>, <see cref="ulong"/>,
/// <see cref="float"/>, <see cref="double"/> and <see cref="decimal"/>. Other types compare only with
/// a threshold of the same type, through that type's own <see cref="IComparable{T}"/>.
/// </para>
/// </remarks>
internal static class NumericComparer
{
    private enum NumericKind
    {
        None,
        Integer,
        Decimal,
        FloatingPoint,
    }

    /// <summary>
    /// Compares <paramref name="value"/> with <paramref name="threshold"/>.
    /// </summary>
    /// <returns>
    /// <c>true</c> with the sign of the comparison in <paramref name="comparison"/>, or <c>false</c> when
    /// the operands cannot be ordered: either one is <see cref="double.NaN"/>/<see cref="float.NaN"/>, or
    /// the types are neither the same nor both built-in numeric types. Callers treat <c>false</c> as a
    /// failed rule; it must never be read as a pass.
    /// </returns>
    internal static bool TryCompare<TValue, TThreshold>(TValue value, TThreshold threshold, out int comparison)
        where TThreshold : IComparable<TThreshold>
    {
        // Same type: the type's own ordering is exact and does not box. NaN is excluded because
        // double.CompareTo orders NaN below every number, which would let NaN pass LessThan.
        if (value is TThreshold sameType)
        {
            if (IsNaN(sameType) || IsNaN(threshold))
            {
                comparison = 0;
                return false;
            }

            comparison = sameType.CompareTo(threshold);
            return true;
        }

        return TryCompareNumbers(value, threshold, out comparison);
    }

    /// <summary>
    /// Non-generic counterpart of <see cref="TryCompare{TValue, TThreshold}"/> for the
    /// <see cref="IComparable"/>-typed guard overloads.
    /// </summary>
    internal static bool TryCompare(object? value, IComparable? threshold, out int comparison)
    {
        if (value is IComparable comparable && threshold is not null && value.GetType() == threshold.GetType())
        {
            if (IsNaN(value) || IsNaN(threshold))
            {
                comparison = 0;
                return false;
            }

            comparison = comparable.CompareTo(threshold);
            return true;
        }

        return TryCompareNumbers(value, threshold, out comparison);
    }

    private static bool TryCompareNumbers(object? left, object? right, out int comparison)
    {
        comparison = 0;
        var leftKind = GetKind(left);
        var rightKind = GetKind(right);
        if (leftKind == NumericKind.None || rightKind == NumericKind.None)
        {
            return false;
        }

        // The pattern checks above guarantee both operands are non-null built-in numbers from here on.
        if (leftKind == NumericKind.FloatingPoint || rightKind == NumericKind.FloatingPoint)
        {
            // ponytail: integers above 2^53 lose precision as double; exact mixed long/double ordering
            // is only worth adding if someone guards such values against fractional thresholds.
            var leftDouble = ((IConvertible)left!).ToDouble(CultureInfo.InvariantCulture);
            var rightDouble = ((IConvertible)right!).ToDouble(CultureInfo.InvariantCulture);
            if (double.IsNaN(leftDouble) || double.IsNaN(rightDouble))
            {
                return false;
            }

            comparison = leftDouble.CompareTo(rightDouble);
            return true;
        }

        if (leftKind == NumericKind.Decimal || rightKind == NumericKind.Decimal)
        {
            // Every integer type converts to decimal exactly (ulong.MaxValue < decimal.MaxValue).
            comparison = ((IConvertible)left!).ToDecimal(CultureInfo.InvariantCulture)
                .CompareTo(((IConvertible)right!).ToDecimal(CultureInfo.InvariantCulture));
            return true;
        }

        // Int128 holds every value of every built-in integer type, signed or unsigned, so a negative
        // long and a large ulong still order correctly.
        comparison = ToInt128(left!).CompareTo(ToInt128(right!));
        return true;
    }

    private static NumericKind GetKind(object? value) => value switch
    {
        sbyte or byte or short or ushort or int or uint or long or ulong => NumericKind.Integer,
        decimal => NumericKind.Decimal,
        float or double => NumericKind.FloatingPoint,
        _ => NumericKind.None,
    };

    private static Int128 ToInt128(object value) => value switch
    {
        sbyte number => number,
        byte number => number,
        short number => number,
        ushort number => number,
        int number => number,
        uint number => number,
        long number => number,
        ulong number => number,
        _ => throw new ArgumentException($"{value.GetType().Name} is not a built-in integer type.", nameof(value)),
    };

    private static bool IsNaN<T>(T value) =>
        (value is double doubleValue && double.IsNaN(doubleValue)) ||
        (value is float floatValue && float.IsNaN(floatValue));
}
