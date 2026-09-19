namespace Moongazing.OrionGuard.Utilities;

/// <summary>
/// Brings a <see cref="DateTime"/> onto the UTC timeline before a guard compares it with
/// <see cref="DateTime.UtcNow"/>.
/// </summary>
/// <remarks>
/// <see cref="DateTime"/> comparison ignores <see cref="DateTime.Kind"/> and compares raw ticks, so a
/// local wall-clock time compared with <see cref="DateTime.UtcNow"/> was off by the machine's UTC offset:
/// <c>DateTime.Now</c> looked like the future east of Greenwich. Local values are therefore converted.
/// <see cref="DateTimeKind.Unspecified"/> values carry no zone to convert from; the date guards treat them
/// as UTC, which is the library's documented convention.
/// </remarks>
internal static class DateTimeNormalization
{
    internal static DateTime ToUtc(DateTime value) =>
        value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value;
}
