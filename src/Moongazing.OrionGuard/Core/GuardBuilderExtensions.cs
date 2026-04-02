using Moongazing.OrionGuard.Exceptions;

namespace Moongazing.OrionGuard.Core;

public static class GuardBuilderExtensions
{
    public static GuardBuilder<T> GreaterThan<T>(this GuardBuilder<T> builder, T min)
        where T : IComparable<T>
    {
        if (builder.Value.CompareTo(min) <= 0)
            ThrowHelper.ThrowGreaterThan(builder.ParameterName, min!);
        return builder;
    }

    public static GuardBuilder<T> LessThan<T>(this GuardBuilder<T> builder, T max)
        where T : IComparable<T>
    {
        if (builder.Value.CompareTo(max) >= 0)
            ThrowHelper.ThrowLessThan(builder.ParameterName, max!);
        return builder;
    }
}