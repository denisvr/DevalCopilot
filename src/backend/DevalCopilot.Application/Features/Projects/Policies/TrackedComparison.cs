using System.Text;

namespace DevalCopilot.Application.Features.Projects.Policies;

/// <summary>
/// The deterministic host comparison of two owned text snapshots of one tracked file, in unified-diff shape. It is built only
/// from the two strings: a linear common-prefix scan, a linear common-suffix scan that never overlaps it, one complete
/// replacement hunk for whatever remains in the middle, and at most <see cref="ContextLines"/> unchanged context lines at each
/// edge. A line is its exact text including its terminator, so a different terminator, or a missing final newline, is a
/// difference and is stated with the standard marker. Nothing is copied from a repository patch: paths are written in the
/// canonical quoted form by this class and hunk ranges are plain numbers. This is NOT Git's minimal or filter-normalized patch:
/// unchanged lines inside the replaced middle appear as removed and added, and line-ending-only differences remain visible.
/// </summary>
internal static class TrackedComparison
{
    internal const int ContextLines = 3;

    private const string NoNewlineMarker = "\\ No newline at end of file\n";

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>The block for one file, or null when the two snapshots are identical. <paramref name="before"/> null means the path
    /// is not in the baseline (an addition); <paramref name="after"/> null means it is absent now (a deletion).</summary>
    internal static string? Compose(string path, string? before, string? after)
    {
        if (before is null && after is null)
        {
            return null;
        }

        if (before is not null && after is not null && string.Equals(before, after, StringComparison.Ordinal))
        {
            return null;
        }

        var builder = new StringBuilder();
        var quotedOld = Quote("a/" + path);
        var quotedNew = Quote("b/" + path);
        builder.Append("diff --git ").Append(quotedOld).Append(' ').Append(quotedNew).Append('\n');
        builder.Append("--- ").Append(before is null ? "/dev/null" : quotedOld).Append('\n');
        builder.Append("+++ ").Append(after is null ? "/dev/null" : quotedNew).Append('\n');

        var oldLines = SplitLines(before ?? string.Empty);
        var newLines = SplitLines(after ?? string.Empty);
        if (oldLines.Count == 0 && newLines.Count == 0)
        {
            // An empty file added or removed: a header, and no hunk.
            return builder.ToString();
        }

        var prefix = 0;
        var limit = Math.Min(oldLines.Count, newLines.Count);
        while (prefix < limit && string.Equals(oldLines[prefix], newLines[prefix], StringComparison.Ordinal))
        {
            prefix++;
        }

        var suffix = 0;
        while (suffix < limit - prefix
            && string.Equals(oldLines[oldLines.Count - 1 - suffix], newLines[newLines.Count - 1 - suffix], StringComparison.Ordinal))
        {
            suffix++;
        }

        var contextBefore = Math.Min(ContextLines, prefix);
        var contextAfter = Math.Min(ContextLines, suffix);
        var firstLine = prefix - contextBefore;
        var oldMiddleEnd = oldLines.Count - suffix;
        var newMiddleEnd = newLines.Count - suffix;
        var oldCount = (oldMiddleEnd - prefix) + contextBefore + contextAfter;
        var newCount = (newMiddleEnd - prefix) + contextBefore + contextAfter;

        builder.Append("@@ -").Append(Range(firstLine, oldCount)).Append(" +").Append(Range(firstLine, newCount)).Append(" @@\n");
        for (var index = firstLine; index < prefix; index++)
        {
            AppendLine(builder, ' ', oldLines[index]);
        }

        for (var index = prefix; index < oldMiddleEnd; index++)
        {
            AppendLine(builder, '-', oldLines[index]);
        }

        for (var index = prefix; index < newMiddleEnd; index++)
        {
            AppendLine(builder, '+', newLines[index]);
        }

        for (var index = oldMiddleEnd; index < oldMiddleEnd + contextAfter; index++)
        {
            AppendLine(builder, ' ', oldLines[index]);
        }

        return builder.ToString();
    }

    /// <summary>The 1-based start (or, for an empty side, the number of lines before it) and the explicit count.</summary>
    private static string Range(int zeroBasedFirst, int count) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{(count == 0 ? zeroBasedFirst : zeroBasedFirst + 1)},{count}");

    private static void AppendLine(StringBuilder builder, char prefix, string line)
    {
        builder.Append(prefix).Append(line);
        if (!line.EndsWith('\n'))
        {
            builder.Append('\n').Append(NoNewlineMarker);
        }
    }

    /// <summary>Lines with their terminators kept; only a final line may lack one. Empty text has no lines.</summary>
    internal static List<string> SplitLines(string text)
    {
        var lines = new List<string>();
        var start = 0;
        while (start < text.Length)
        {
            var newline = text.IndexOf('\n', start);
            var end = newline < 0 ? text.Length : newline + 1;
            lines.Add(text[start..end]);
            start = end;
        }

        return lines;
    }

    /// <summary>The path as Git writes a header path: bare when it needs no quoting, otherwise C-style quoted with octal escapes
    /// for control characters and every non-ASCII UTF-8 byte.</summary>
    internal static string Quote(string path)
    {
        var needsQuotes = false;
        foreach (var character in path)
        {
            if (character < 0x20 || character == 0x7F || character == '"' || character == '\\' || character > 0x7E)
            {
                needsQuotes = true;
                break;
            }
        }

        if (!needsQuotes)
        {
            return path;
        }

        var builder = new StringBuilder("\"");
        foreach (var value in StrictUtf8.GetBytes(path))
        {
            switch (value)
            {
                case (byte)'"':
                    builder.Append("\\\"");
                    break;
                case (byte)'\\':
                    builder.Append("\\\\");
                    break;
                case 7:
                    builder.Append("\\a");
                    break;
                case 8:
                    builder.Append("\\b");
                    break;
                case 9:
                    builder.Append("\\t");
                    break;
                case 10:
                    builder.Append("\\n");
                    break;
                case 11:
                    builder.Append("\\v");
                    break;
                case 12:
                    builder.Append("\\f");
                    break;
                case 13:
                    builder.Append("\\r");
                    break;
                case < 0x20 or 0x7F or > 0x7E:
                    builder.Append('\\').Append(Convert.ToString(value, 8).PadLeft(3, '0'));
                    break;
                default:
                    builder.Append((char)value);
                    break;
            }
        }

        return builder.Append('"').ToString();
    }
}
