namespace Moongazing.OrionGuard.Outbox.Dashboard;

using System.Buffers.Text;
using System.Globalization;
using System.Text;

/// <summary>
/// Opaque cursor token used by the v6.5.8 cursor-pagination endpoint
/// (<c>GET /_orion/outbox/failed/cursor</c>). The cursor encodes the last row's sort key
/// + id + retry count + axis so the next page predicate is a deterministic
/// <c>WHERE (sortKey, id) &gt; (lastSortKey, lastId)</c> instead of the offset-pagination
/// <c>OFFSET</c> that grows expensive on large failed-message tables.
/// </summary>
/// <remarks>
/// <para>
/// Wire format: <c>base64Url(LastOccurredOnUtc.Ticks | LastId | LastRetryCount | Sort)</c>
/// joined with <c>'|'</c> separators. The encoding is opaque to clients - a future
/// schema change can rev the format without affecting the public API.
/// </para>
/// <para>
/// The cursor is NOT cryptographically signed; consumers MUST treat it as a soft hint
/// for paging and re-validate any business rules on the rows the page returns. The token
/// is bound to the sort axis it was issued under; a caller that switches sort mid-paging
/// effectively restarts from page one.
/// </para>
/// </remarks>
internal readonly record struct OutboxFailedCursor(
    long LastOccurredOnUtcTicks,
    Guid LastId,
    int LastRetryCount,
    OutboxFailedListingSort Sort)
{
    public string Encode()
    {
        var raw = string.Join('|',
            LastOccurredOnUtcTicks.ToString(CultureInfo.InvariantCulture),
            LastId.ToString("N"),
            LastRetryCount.ToString(CultureInfo.InvariantCulture),
            ((int)Sort).ToString(CultureInfo.InvariantCulture));
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw))
            // base64url - replace + / with - _ and strip padding so the cursor fits a
            // URL query string without escaping.
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    public static bool TryDecode(string? raw, out OutboxFailedCursor cursor)
    {
        cursor = default;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }
        var padded = raw.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(padded);
        }
        catch (FormatException)
        {
            return false;
        }
        var parts = Encoding.UTF8.GetString(bytes).Split('|');
        if (parts.Length != 4)
        {
            return false;
        }
        if (!long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticks)
            || !Guid.TryParseExact(parts[1], "N", out var id)
            || !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var retries)
            || !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var sortRaw)
            || !Enum.IsDefined(typeof(OutboxFailedListingSort), sortRaw))
        {
            return false;
        }
        cursor = new OutboxFailedCursor(ticks, id, retries, (OutboxFailedListingSort)sortRaw);
        return true;
    }
}
