using System.Text;

namespace DevalCopilot.Application.Features.Runs.Policies;

/// <summary>
/// Splits the already captured <c>git diff --no-ext-diff --no-textconv --no-renames --binary HEAD</c> text into file
/// blocks and validated whole hunks, without another Git call. A boundary is only ever taken where the format makes
/// it certain: a line starting with <c>diff --git </c> or <c>@@ </c> at column 0 cannot occur inside a hunk (content
/// lines carry a <c>' '</c>, <c>'+'</c>, <c>'-'</c>, or <c>'\'</c> prefix), and a hunk ends exactly where its header's
/// old and new line counts are consumed. Anything else (an unknown header line, counts that do not match, a header
/// path that cannot be decoded, a prefix other than <c>a/</c> and <c>b/</c>) makes that one file
/// <see cref="TrackedDiffFileKind.Unsupported"/> instead of a guess.
/// </summary>
internal static class TrackedDiffParser
{
    private const string FileMarker = "diff --git ";
    private const string BinaryPatchMarker = "GIT binary patch";
    private const string BinaryFilesMarker = "Binary files ";

    private static readonly string[] HeaderPrefixes =
    [
        "old mode ", "new mode ", "deleted file mode ", "new file mode ", "index ", "--- ", "+++ ",
        "similarity index ", "dissimilarity index ", "rename from ", "rename to ", "copy from ", "copy to ",
    ];

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    internal static ParsedTrackedDiff Parse(string diff)
    {
        var lines = SplitLines(diff);
        var containsBinary = lines.Any(line => IsBinaryMarker(line));
        if (diff.Length == 0)
        {
            return new ParsedTrackedDiff(true, [], false);
        }

        if (!diff.StartsWith(FileMarker, StringComparison.Ordinal))
        {
            return new ParsedTrackedDiff(false, [], containsBinary);
        }

        var files = new List<TrackedDiffFile>();
        var blockStart = 0;
        for (var index = 1; index <= lines.Count; index++)
        {
            if (index == lines.Count || lines[index].StartsWith(FileMarker, StringComparison.Ordinal))
            {
                files.Add(ParseBlock(lines, blockStart, index));
                blockStart = index;
            }
        }

        return new ParsedTrackedDiff(true, files, containsBinary);
    }

    private static bool IsBinaryMarker(string line) =>
        line.StartsWith(BinaryPatchMarker, StringComparison.Ordinal)
        || line.StartsWith(BinaryFilesMarker, StringComparison.Ordinal);

    private static List<string> SplitLines(string text)
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

    private static TrackedDiffFile ParseBlock(List<string> lines, int from, int to)
    {
        var path = TryParsePath(lines[from][FileMarker.Length..].TrimEnd('\n'));
        var header = new StringBuilder(lines[from]);
        var index = from + 1;
        while (index < to
            && !lines[index].StartsWith("@@ ", StringComparison.Ordinal)
            && !IsBinaryMarker(lines[index]))
        {
            if (!HeaderPrefixes.Any(prefix => lines[index].StartsWith(prefix, StringComparison.Ordinal)))
            {
                return Unsupported(path, "unsupported_format");
            }

            header.Append(lines[index]);
            index++;
        }

        if (path is null)
        {
            return Unsupported(null, "header_unparseable");
        }

        if (index == to)
        {
            return new TrackedDiffFile(path, TrackedDiffFileKind.MetadataOnly, header.ToString(), [], null);
        }

        if (IsBinaryMarker(lines[index]))
        {
            return new TrackedDiffFile(path, TrackedDiffFileKind.Binary, header.ToString(), [], "binary");
        }

        var hunks = new List<string>();
        while (index < to)
        {
            var hunkStart = index;
            if (!TryParseHunkHeader(lines[index], out var oldLeft, out var newLeft))
            {
                return Unsupported(path, "malformed_hunk");
            }

            index++;
            while (oldLeft > 0 || newLeft > 0)
            {
                if (index >= to || lines[index].Length == 0)
                {
                    return Unsupported(path, "malformed_hunk");
                }

                switch (lines[index][0])
                {
                    case ' ':
                        oldLeft--;
                        newLeft--;
                        break;
                    case '-':
                        oldLeft--;
                        break;
                    case '+':
                        newLeft--;
                        break;
                    case '\\':
                        break;
                    default:
                        return Unsupported(path, "malformed_hunk");
                }

                if (oldLeft < 0 || newLeft < 0)
                {
                    return Unsupported(path, "malformed_hunk");
                }

                index++;
            }

            while (index < to && lines[index].StartsWith('\\'))
            {
                index++;
            }

            hunks.Add(string.Concat(lines.GetRange(hunkStart, index - hunkStart)));
        }

        return new TrackedDiffFile(path, TrackedDiffFileKind.Text, header.ToString(), hunks, null);
    }

    private static TrackedDiffFile Unsupported(string? path, string reason) =>
        new(path, TrackedDiffFileKind.Unsupported, string.Empty, [], reason);

    private static bool TryParseHunkHeader(string line, out int oldCount, out int newCount)
    {
        oldCount = 0;
        newCount = 0;
        var index = 0;
        return Consume(line, ref index, "@@ -")
            && TryReadRange(line, ref index, out oldCount)
            && Consume(line, ref index, " +")
            && TryReadRange(line, ref index, out newCount)
            && Consume(line, ref index, " @@");
    }

    private static bool Consume(string text, ref int index, string expected)
    {
        if (string.CompareOrdinal(text, index, expected, 0, expected.Length) != 0)
        {
            return false;
        }

        index += expected.Length;
        return true;
    }

    /// <summary>A range is <c>start</c> or <c>start,count</c>; a missing count means one line.</summary>
    private static bool TryReadRange(string text, ref int index, out int count)
    {
        count = 1;
        if (!TryReadNumber(text, ref index, out _))
        {
            return false;
        }

        return index < text.Length && text[index] == ','
            ? Consume(text, ref index, ",") && TryReadNumber(text, ref index, out count)
            : true;
    }

    private static bool TryReadNumber(string text, ref int index, out int value)
    {
        value = 0;
        var digits = 0;
        while (index < text.Length && char.IsAsciiDigit(text[index]))
        {
            if (++digits > 9)
            {
                return false;
            }

            value = (value * 10) + (text[index] - '0');
            index++;
        }

        return digits > 0;
    }

    /// <summary>The path in <c>diff --git a/P b/P</c>. Only the default <c>a/</c> and <c>b/</c> prefixes and an
    /// identical path on both sides (no renames are captured) are accepted; an unquoted path is split by length, so
    /// there is exactly one reading.</summary>
    internal static string? TryParsePath(string rest)
    {
        if (rest.StartsWith('"'))
        {
            if (!TryReadQuoted(rest, out var first, out var end) || end >= rest.Length || rest[end] != ' ')
            {
                return null;
            }

            var secondText = rest[(end + 1)..];
            string second;
            if (secondText.StartsWith('"'))
            {
                if (!TryReadQuoted(secondText, out second!, out var secondEnd) || secondEnd != secondText.Length)
                {
                    return null;
                }
            }
            else
            {
                second = secondText;
            }

            return first.StartsWith("a/", StringComparison.Ordinal) && second.StartsWith("b/", StringComparison.Ordinal)
                && string.Equals(first[2..], second[2..], StringComparison.Ordinal) && first.Length > 2
                ? first[2..]
                : null;
        }

        var length = rest.Length;
        if (length < 7 || (length - 5) % 2 != 0)
        {
            return null;
        }

        var pathLength = (length - 5) / 2;
        return rest.StartsWith("a/", StringComparison.Ordinal)
            && rest[2 + pathLength] == ' '
            && string.CompareOrdinal(rest, 3 + pathLength, "b/", 0, 2) == 0
            && string.CompareOrdinal(rest, 2, rest, 5 + pathLength, pathLength) == 0
            ? rest.Substring(2, pathLength)
            : null;
    }

    /// <summary>Reads one C-style quoted token as Git writes it (octal escapes for non-ASCII bytes and control
    /// characters). Invalid escapes or bytes that are not UTF-8 fail closed.</summary>
    private static bool TryReadQuoted(string text, out string value, out int endExclusive)
    {
        value = string.Empty;
        endExclusive = 0;
        var bytes = new List<byte>();
        var index = 1;
        while (index < text.Length)
        {
            var current = text[index];
            if (current == '"')
            {
                try
                {
                    value = StrictUtf8.GetString([.. bytes]);
                }
                catch (DecoderFallbackException)
                {
                    return false;
                }

                endExclusive = index + 1;
                return true;
            }

            if (current != '\\')
            {
                var width = char.IsHighSurrogate(current) && index + 1 < text.Length ? 2 : 1;
                bytes.AddRange(Encoding.UTF8.GetBytes(text.AsSpan(index, width).ToString()));
                index += width;
                continue;
            }

            if (index + 1 >= text.Length)
            {
                return false;
            }

            var escape = text[index + 1];
            byte? simple = escape switch
            {
                'a' => 7,
                'b' => 8,
                'f' => 12,
                'n' => 10,
                'r' => 13,
                't' => 9,
                'v' => 11,
                '\\' => (byte)'\\',
                '"' => (byte)'"',
                _ => null,
            };

            if (simple is { } single)
            {
                bytes.Add(single);
                index += 2;
            }
            else if (escape is >= '0' and <= '3' && index + 3 < text.Length
                && text[index + 2] is >= '0' and <= '7' && text[index + 3] is >= '0' and <= '7')
            {
                bytes.Add((byte)(((escape - '0') << 6) | ((text[index + 2] - '0') << 3) | (text[index + 3] - '0')));
                index += 4;
            }
            else
            {
                return false;
            }
        }

        return false;
    }
}
