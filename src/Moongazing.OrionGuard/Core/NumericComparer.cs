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
/// <para>
/// Integers compare exactly with each other, with <see cref="decimal"/>, and with floating-point values
/// (an integer above 2^53 is not rounded). A <see cref="decimal"/> compared with a <see cref="double"/> or
/// <see cref="float"/> uses the floating-point value converted to decimal (15 significant digits), so
/// <c>0.1m</c> equals the literal <c>0.1</c>.
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
        if (leftKind == NumericKind.FloatingPoint && rightKind == NumericKind.FloatingPoint)
        {
            // float widens to double exactly, so a double comparison is exact for float/double pairs.
            var leftDouble = ((IConvertible)left!).ToDouble(CultureInfo.InvariantCulture);
            var rightDouble = ((IConvertible)right!).ToDouble(CultureInfo.InvariantCulture);
            if (double.IsNaN(leftDouble) || double.IsNaN(rightDouble))
            {
                return false;
            }

            comparison = leftDouble.CompareTo(rightDouble);
            return true;
        }

        if (leftKind == NumericKind.FloatingPoint)
        {
            var floating = ((IConvertible)left!).ToDouble(CultureInfo.InvariantCulture);
            if (!TryCompareWithFloatingPoint(right!, rightKind, floating, out var reversed))
            {
                return false;
            }

            comparison = -reversed;
            return true;
        }

        if (rightKind == NumericKind.FloatingPoint)
        {
            var floating = ((IConvertible)right!).ToDouble(CultureInfo.InvariantCulture);
            return TryCompareWithFloatingPoint(left!, leftKind, floating, out comparison);
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

    // 2^127 and 2^96 are exact doubles: every Int128 lies strictly below the first, every decimal strictly below the second.
    private const double Int128Bound = 1.7014118346046923E+38;
    private const double DecimalBound = 7.922816251426434E+28;

    /// <summary>
    /// Compares an integer or decimal <paramref name="exact"/> with a double without rounding the exact side.
    /// </summary>
    private static bool TryCompareWithFloatingPoint(object exact, NumericKind exactKind, double floating, out int comparison)
    {
        comparison = 0;
        if (double.IsNaN(floating))
        {
            return false;
        }

        if (exactKind == NumericKind.Integer)
        {
            // Integers compare exactly: an integer above 2^53 must not collapse onto a neighbouring double.
            if (floating >= Int128Bound)
            {
                comparison = -1;
                return true;
            }

            if (floating < -Int128Bound)
            {
                comparison = 1;
                return true;
            }

            var integer = ToInt128(exact);
            var floor = Math.Floor(floating);
            var floorInteger = (Int128)floor;
            comparison = integer != floorInteger
                ? integer.CompareTo(floorInteger)
                : floating == floor ? 0 : -1;
            return true;
        }

        // Decimal: convert the double to decimal, which keeps its 15 significant digits. A decimal value
        // is usually compared with a double literal, and 0.1m must equal the literal 0.1 rather than the
        // binary value 0.1000000000000000055…
        if (floating >= DecimalBound)
        {
            comparison = -1;
            return true;
        }

        if (floating <= -DecimalBound)
        {
            comparison = 1;
            return true;
        }

        comparison = ((IConvertible)exact).ToDecimal(CultureInfo.InvariantCulture).CompareTo((decimal)floating);
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
