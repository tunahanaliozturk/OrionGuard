using System.Text;

namespace Moongazing.OrionGuard.Migration;

/// <summary>
/// Produces a compact, unified-diff-style textual rendering of a single file's changes for the
/// report / dry-run output. This is a readability aid, not a machine-applicable patch, so it uses
/// a simple line-by-line longest-common-subsequence walk rather than a full hunk format.
/// </summary>
public static class UnifiedDiff
{
    /// <summary>
    /// Renders the difference between <paramref name="original"/> and <paramref name="migrated"/>
    /// as a unified-style diff with a header naming the file.
    /// </summary>
    public static string Render(string filePath, string original, string migrated)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(migrated);

        var oldLines = SplitLines(original);
        var newLines = SplitLines(migrated);

        var builder = new StringBuilder();
        builder.Append("--- ").Append(filePath).Append(" (original)").Append('\n');
        builder.Append("+++ ").Append(filePath).Append(" (migrated)").Append('\n');

        var lcs = LongestCommonSubsequence(oldLines, newLines);

        var i = 0;
        var j = 0;
        foreach (var (oldIndex, newIndex) in lcs)
        {
            while (i < oldIndex)
            {
                builder.Append('-').Append(oldLines[i]).Append('\n');
                i++;
            }

            while (j < newIndex)
            {
                builder.Append('+').Append(newLines[j]).Append('\n');
                j++;
            }

            builder.Append(' ').Append(oldLines[i]).Append('\n');
            i++;
            j++;
        }

        while (i < oldLines.Count)
        {
            builder.Append('-').Append(oldLines[i]).Append('\n');
            i++;
        }

        while (j < newLines.Count)
        {
            builder.Append('+').Append(newLines[j]).Append('\n');
            j++;
        }

        return builder.ToString();
    }

    private static List<string> SplitLines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Split('\n')
            .ToList();

    /// <summary>
    /// Returns the matched index pairs (old line index, new line index) that form a longest common
    /// subsequence of the two line lists. Anything not in a pair is an addition or a removal.
    /// </summary>
    private static List<(int OldIndex, int NewIndex)> LongestCommonSubsequence(
        List<string> oldLines, List<string> newLines)
    {
        var n = oldLines.Count;
        var m = newLines.Count;
        var table = new int[n + 1, m + 1];

        for (var a = n - 1; a >= 0; a--)
        {
            for (var b = m - 1; b >= 0; b--)
            {
                table[a, b] = string.Equals(oldLines[a], newLines[b], StringComparison.Ordinal)
                    ? table[a + 1, b + 1] + 1
                    : Math.Max(table[a + 1, b], table[a, b + 1]);
            }
        }

        var pairs = new List<(int, int)>();
        var x = 0;
        var y = 0;
        while (x < n && y < m)
        {
            if (string.Equals(oldLines[x], newLines[y], StringComparison.Ordinal))
            {
                pairs.Add((x, y));
                x++;
                y++;
            }
            else if (table[x + 1, y] >= table[x, y + 1])
            {
                x++;
            }
            else
            {
                y++;
            }
        }

        return pairs;
    }
}
