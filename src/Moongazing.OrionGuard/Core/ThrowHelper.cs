using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Moongazing.OrionGuard.Exceptions;

namespace Moongazing.OrionGuard.Core;

/// <summary>
/// Centralized throw helpers that keep validation fast paths small for JIT inlining.
/// All methods are marked [DoesNotReturn] and [StackTraceHidden] for clean stack traces.
/// </summary>
internal static class ThrowHelper
{
    [DoesNotReturn]
    [StackTraceHidden]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowNullValue(string parameterName)
        => throw new NullValueException(parameterName);

    [DoesNotReturn]
    [StackTraceHidden]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowEmptyString(string parameterName)
        => throw new EmptyStringException(parameterName);

    [DoesNotReturn]
    [StackTraceHidden]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowNegative(string parameterName)
        => throw new NegativeException(parameterName);

    [DoesNotReturn]
    [StackTraceHidden]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowNegativeDecimal(string parameterName)
        => throw new NegativeDecimalException(parameterName);

    [DoesNotReturn]
    [StackTraceHidden]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowLessThan(string parameterName, object minValue)
        => throw new LessThanException(parameterName, minValue);

    [DoesNotReturn]
    [StackTraceHidden]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowGreaterThan(string parameterName, object maxValue)
        => throw new GreaterThanException(parameterName, maxValue);

    [DoesNotReturn]
    [StackTraceHidden]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowOutOfRange(string parameterName, object min, object max)
        => throw new OutOfRangeException(parameterName, min, max);

    [DoesNotReturn]
    [StackTraceHidden]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowInvalidEmail(string parameterName)
        => throw new InvalidEmailException(parameterName);

    [DoesNotReturn]
    [StackTraceHidden]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowInvalidUrl(string parameterName)
        => throw new InvalidUrlException(parameterName);

    [DoesNotReturn]
    [StackTraceHidden]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowInvalidIp(string parameterName)
        => throw new InvalidIpException(parameterName);

    [DoesNotReturn]
    [StackTraceHidden]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowInvalidGuid(string parameterName)
        => throw new InvalidGuidException(parameterName);

    [DoesNotReturn]
    [StackTraceHidden]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowPastDate(string parameterName)
        => throw new PastDateException(parameterName);

    [DoesNotReturn]
    [StackTraceHidden]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowFutureDate(string parameterName)
        => throw new FutureDateException(parameterName);

    [DoesNotReturn]
    [StackTraceHidden]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowFalse(string parameterName)
        => throw new FalseException(parameterName);

    [DoesNotReturn]
    [StackTraceHidden]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowTrue(string parameterName)
        => throw new TrueException(parameterName);

    [DoesNotReturn]
    [StackTraceHidden]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowZeroValue(string parameterName)
        => throw new ZeroValueException(parameterName);

    [DoesNotReturn]
    [StackTraceHidden]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowInvalidXml(string parameterName)
        => throw new InvalidXmlException(parameterName);

    [DoesNotReturn]
    [StackTraceHidden]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowWeakPassword(string parameterName)
        => throw new WeakPasswordException(parameterName);

    [DoesNotReturn]
    [StackTraceHidden]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowFileNotExists(string parameterName)
        => throw new FileNotExistsException(parameterName);

    [DoesNotReturn]
    [StackTraceHidden]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowEmptyFile(string parameterName)
        => throw new EmptyFileException(parameterName);

    [DoesNotReturn]
    [StackTraceHidden]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowInvalidEnum(string parameterName)
        => throw new InvalidEnumValueException(parameterName);

    [DoesNotReturn]
    [StackTraceHidden]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowRegexMismatch(string parameterName, string pattern)
        => throw new RegexMismatchException(parameterName, pattern);

    [DoesNotReturn]
    [StackTraceHidden]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowGuardException(string message)
        => throw new GuardException(message);

    [DoesNotReturn]
    [StackTraceHidden]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowArgumentException(string message, string parameterName)
        => throw new ArgumentException(message, parameterName);

    [DoesNotReturn]
    [StackTraceHidden]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowArgumentNullException(string parameterName)
        => throw new ArgumentNullException(parameterName);

    [DoesNotReturn]
    [StackTraceHidden]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowArgumentOutOfRangeException(string parameterName, object actualValue, string message)
        => throw new ArgumentOutOfRangeException(parameterName, actualValue, message);
}
