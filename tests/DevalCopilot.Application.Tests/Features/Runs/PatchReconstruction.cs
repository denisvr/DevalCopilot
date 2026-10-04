namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>An independent, deliberately plain applier of the single-file unified patches the host comparison writes. It shares no code
/// with the production writer: it parses the numeric hunk ranges, checks every context and removed line against the baseline it is given,
/// honours the no-final-newline marker on either side, and returns the resulting text, so a test can prove an accepted whole
/// comparison reconstructs exactly the intended after text from exactly the before text.</summary>
internal static class PatchReconstruction
{
    internal sealed record Result(string? Text, bool Created, bool Deleted);

    /// <summary>Applies <paramref name="patch"/> to <paramref name="before"/> (null: the path is absent). Throws on any mismatch.</summary>
    public static Result Apply(string patch, string? before)
    {
        var raw = Split(patch);
        var index = 0;
        var created = false;
        var deleted = false;
        while (index < raw.Count && !raw[index].StartsWith("@@ ", StringComparison.Ordinal))
        {
            created |= raw[index] == "--- /dev/null\n";
            deleted |= raw[index] == "+++ /dev/null\n";
            index++;
        }

        Assert(created == (before is null), "a patch that creates the file is applied to an absent baseline");
        var source = before is null ? [] : Split(before);
        var output = new List<string>();
        var cursor = 0;
        while (index < raw.Count)
        {
            var (oldStart, oldCount, newStart, newCount) = Header(raw[index]);
            index++;
            var skipTo = oldCount == 0 ? oldStart : oldStart - 1;
            Assert(skipTo >= cursor && skipTo <= source.Count, "hunks are ordered and inside the baseline");
            while (cursor < skipTo)
            {
                output.Add(source[cursor++]);
            }

            Assert((newCount == 0 ? newStart : newStart - 1) == output.Count, "the new range starts where the output stands");
            int seenOld = 0, seenNew = 0;
            while (seenOld < oldCount || seenNew < newCount)
            {
                var line = raw[index++];
                var marker = index < raw.Count && raw[index].StartsWith('\\');
                var content = line[1..];
                if (marker)
                {
                    Assert(raw[index] == "\\ No newline at end of file\n" && content.EndsWith('\n'), "the marker follows a newline-terminated patch line");
                    content = content[..^1];
                    index++;
                }

                switch (line[0])
                {
                    case ' ':
                        Assert(cursor < source.Count && source[cursor] == content, "a context line equals the baseline line");
                        output.Add(source[cursor++]);
                        seenOld++;
                        seenNew++;
                        break;
                    case '-':
                        Assert(cursor < source.Count && source[cursor] == content, "a removed line equals the baseline line");
                        cursor++;
                        seenOld++;
                        break;
                    case '+':
                        output.Add(content);
                        seenNew++;
                        break;
                    default:
                        throw new InvalidOperationException("unknown line prefix");
                }
            }
        }

        while (cursor < source.Count)
        {
            output.Add(source[cursor++]);
        }

        // Only the very last line of a file may lack its terminator.
        for (var line = 0; line < output.Count - 1; line++)
        {
            Assert(output[line].EndsWith('\n'), "only the last line may lack a terminator");
        }

        return new Result(deleted ? null : string.Concat(output), created, deleted);
    }

    private static (int OldStart, int OldCount, int NewStart, int NewCount) Header(string line)
    {
        // "@@ -a,b +c,d @@\n": explicit counts only.
        var parts = line.TrimEnd('\n').Split(' ');
        Assert(parts.Length == 4 && parts[0] == "@@" && parts[3] == "@@" && parts[1][0] == '-' && parts[2][0] == '+', "hunk header shape");
        var old = parts[1][1..].Split(',');
        var @new = parts[2][1..].Split(',');
        Assert(old.Length == 2 && @new.Length == 2, "explicit numeric ranges");
        return (int.Parse(old[0]), int.Parse(old[1]), int.Parse(@new[0]), int.Parse(@new[1]));
    }

    private static List<string> Split(string text)
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

    private static void Assert(bool condition, string what)
    {
        if (!condition)
        {
            throw new InvalidOperationException("patch does not reconstruct: " + what);
        }
    }
}
