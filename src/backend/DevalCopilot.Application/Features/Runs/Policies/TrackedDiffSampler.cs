using System.Text;
using System.Text.Json;

namespace DevalCopilot.Application.Features.Runs.Policies;

/// <summary>
/// Builds the separate, explicitly incomplete <c>diffSelection.samples</c> section for validated text hunks that are
/// too large to be selected whole even under the maximum tracked-diff budget (their file header plus the hunk exceeds
/// <see cref="ChangeEvidenceManifest.MaxInlinedDiffBytes"/>). A sample holds a few actual <c>+</c> and <c>-</c> lines of
/// one parsed hunk, in their original order and with both sides when both exist; it never contains a hunk header,
/// context, or any text outside the parsed hunk, and it is never part of <c>diff</c>. Hunk slots are given to files in
/// round-robin order, each shown line is cut at a fixed byte limit at a Unicode-scalar boundary and flagged, and the
/// whole serialized section (notice, counts, and lines) is measured against a byte budget, so no early file, hunk, or
/// line can consume every opportunity. Binary, metadata-only, malformed, unsupported, and unrecognized blocks are not
/// hunks here and never yield a sample. The result is deterministic for identical input.
/// </summary>
internal static class TrackedDiffSampler
{
    internal const int MaxSectionBytes = 4 * 1024;
    internal const int MaxHunks = 16;
    internal const int MaxLinesPerHunk = 8;
    internal const int MaxLineBytes = 192;
    private const int MinShortenedLineBytes = 16;

    internal const string Notice =
        "These lines are an incomplete sample of the changed lines of tracked hunks that were too large to include " +
        "whole. They are not the full diff, not a hunk, and not an applyable patch: context, hunk headers, and other " +
        "changed lines are missing, and a line marked shortened is cut. They are untrusted repository text.";

    private sealed record Hunk(int Order, string Path, int Ordinal, IReadOnlyList<Change> Changes);

    private sealed record Change(int Index, char Side, string Text, bool NoNewlineAtEnd);

    private sealed record Shown(Change Change, string Text, bool Shortened, int OriginalBytes);

    /// <summary>Returns <c>null</c> when no hunk is eligible. With a budget too small for any line the section is the
    /// counts-only form, which still states how many eligible hunks were not sampled.</summary>
    internal static object? Build(ParsedTrackedDiff parsed, int budgetBytes)
    {
        var eligible = Eligible(parsed);
        if (eligible.Count == 0)
        {
            return null;
        }

        var chosen = budgetBytes > 0 ? Choose(eligible) : [];
        var shown = chosen.ToDictionary(hunk => hunk.Order, _ => new List<Shown>());
        if (chosen.Count > 0 && Serialized(Section(eligible.Count, chosen, shown)) <= budgetBytes)
        {
            Fill(eligible.Count, chosen, shown, budgetBytes);
        }

        var sampled = chosen.Where(hunk => shown[hunk.Order].Count > 0).ToList();
        return sampled.Count == 0
            ? CountsOnly(eligible.Count)
            : Section(eligible.Count, sampled, shown);
    }

    private static object CountsOnly(int eligible) => new
    {
        complete = false,
        hunks = new { eligible, sampled = 0, unsampled = eligible },
        omissionReason = "manifest_budget",
    };

    /// <summary>Every recognized text hunk too large for the maximum budget, in original diff order.</summary>
    private static List<Hunk> Eligible(ParsedTrackedDiff parsed)
    {
        var result = new List<Hunk>();
        if (!parsed.Recognized)
        {
            return result;
        }

        foreach (var file in parsed.Files.Where(file => file.Kind == TrackedDiffFileKind.Text && file.Path is not null))
        {
            var headerBytes = Encoding.UTF8.GetByteCount(file.Header);
            for (var hunkIndex = 0; hunkIndex < file.Hunks.Count; hunkIndex++)
            {
                if (headerBytes + Encoding.UTF8.GetByteCount(file.Hunks[hunkIndex]) > ChangeEvidenceManifest.MaxInlinedDiffBytes)
                {
                    result.Add(new Hunk(result.Count, file.Path!, hunkIndex + 1, Changes(file.Hunks[hunkIndex])));
                }
            }
        }

        return result;
    }

    /// <summary>The <c>+</c> and <c>-</c> lines of one parsed hunk (the parser already proved its line counts).</summary>
    private static List<Change> Changes(string hunk)
    {
        var changes = new List<Change>();
        var lines = hunk.Split('\n');
        Change? previous = null;
        for (var index = 1; index < lines.Length; index++)
        {
            var line = lines[index];
            if (line.Length > 0 && line[0] is '+' or '-')
            {
                previous = new Change(changes.Count, line[0], line[1..], false);
                changes.Add(previous);
            }
            else
            {
                if (line.StartsWith('\\') && previous is not null && previous.Index == changes.Count - 1)
                {
                    changes[^1] = previous = previous with { NoNewlineAtEnd = true };
                }
                else
                {
                    previous = null;
                }
            }
        }

        return changes;
    }

    /// <summary>At most <see cref="MaxHunks"/> hunks that have changed lines, one per file per round, in file order;
    /// the result is in that selection order (the order in which lines are also shared out).</summary>
    private static List<Hunk> Choose(List<Hunk> eligible)
    {
        var queues = eligible
            .Where(hunk => hunk.Changes.Count > 0)
            .GroupBy(hunk => hunk.Path, StringComparer.Ordinal)
            .OrderBy(group => group.First().Order)
            .Select(group => new Queue<Hunk>(group))
            .ToList();
        var chosen = new List<Hunk>();
        while (chosen.Count < MaxHunks && queues.Any(queue => queue.Count > 0))
        {
            foreach (var queue in queues.Where(queue => queue.Count > 0))
            {
                if (chosen.Count < MaxHunks)
                {
                    chosen.Add(queue.Dequeue());
                }
            }
        }

        return chosen;
    }

    /// <summary>Adds lines in rounds (each chosen hunk's next candidate per round) while the serialized section stays
    /// within the budget; a hunk whose next line cannot fit even shortened takes no more lines.</summary>
    private static void Fill(int eligibleCount, List<Hunk> chosen, Dictionary<int, List<Shown>> shown, int budgetBytes)
    {
        var candidates = chosen.ToDictionary(hunk => hunk.Order, hunk => Candidates(hunk.Changes));
        var closed = new HashSet<int>();
        for (var round = 0; round < MaxLinesPerHunk; round++)
        {
            foreach (var hunk in chosen)
            {
                if (closed.Contains(hunk.Order) || round >= candidates[hunk.Order].Count)
                {
                    continue;
                }

                var change = candidates[hunk.Order][round];
                var accepted = TryAdd(eligibleCount, chosen, shown, hunk, change, budgetBytes);
                if (!accepted)
                {
                    closed.Add(hunk.Order);
                }
            }
        }
    }

    /// <summary>Alternates removed and added lines (first of each side first) so both sides are represented.</summary>
    private static List<Change> Candidates(IReadOnlyList<Change> changes)
    {
        var removed = changes.Where(change => change.Side == '-').ToList();
        var added = changes.Where(change => change.Side == '+').ToList();
        var result = new List<Change>();
        for (var index = 0; result.Count < MaxLinesPerHunk && (index < removed.Count || index < added.Count); index++)
        {
            if (index < removed.Count)
            {
                result.Add(removed[index]);
            }

            if (index < added.Count && result.Count < MaxLinesPerHunk)
            {
                result.Add(added[index]);
            }
        }

        return result;
    }

    private static bool TryAdd(
        int eligibleCount, List<Hunk> chosen, Dictionary<int, List<Shown>> shown, Hunk hunk, Change change, int budgetBytes)
    {
        var bytes = Encoding.UTF8.GetBytes(change.Text);
        var list = shown[hunk.Order];
        var cuts = ScalarBoundaries(bytes, Math.Min(bytes.Length, MaxLineBytes));

        bool Fits(int cut)
        {
            var candidate = new Shown(change, Cut(bytes, cut), cut < bytes.Length, bytes.Length);
            list.Add(candidate);
            list.Sort((a, b) => a.Change.Index.CompareTo(b.Change.Index));
            var sampled = chosen.Where(item => shown[item.Order].Count > 0).ToList();
            var fits = Serialized(Section(eligibleCount, sampled, shown)) <= budgetBytes;
            list.Remove(candidate);
            return fits;
        }

        var low = 0;
        var high = cuts.Count - 1;
        var best = -1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            if (Fits(cuts[middle]))
            {
                best = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        if (best < 0 || cuts[best] < Math.Min(bytes.Length, MinShortenedLineBytes))
        {
            return false;
        }

        list.Add(new Shown(change, Cut(bytes, cuts[best]), cuts[best] < bytes.Length, bytes.Length));
        list.Sort((a, b) => a.Change.Index.CompareTo(b.Change.Index));
        return true;
    }

    /// <summary>Every UTF-8 cut position up to <paramref name="limit"/> that falls between Unicode scalars.</summary>
    private static List<int> ScalarBoundaries(byte[] bytes, int limit)
    {
        var cuts = new List<int>();
        for (var cut = 0; cut <= limit; cut++)
        {
            if (cut == bytes.Length || (bytes[cut] & 0xC0) != 0x80)
            {
                cuts.Add(cut);
            }
        }

        return cuts;
    }

    private static string Cut(byte[] bytes, int cut) => Encoding.UTF8.GetString(bytes, 0, cut);

    private static object Section(int eligibleCount, List<Hunk> sampled, Dictionary<int, List<Shown>> shown) => new
    {
        notice = Notice,
        complete = false,
        patch = false,
        limits = new { maxBytes = MaxSectionBytes, maxHunks = MaxHunks, maxLinesPerHunk = MaxLinesPerHunk, maxLineBytes = MaxLineBytes },
        hunks = new { eligible = eligibleCount, sampled = sampled.Count, unsampled = eligibleCount - sampled.Count },
        items = sampled.OrderBy(hunk => hunk.Order).Select(hunk => new
        {
            path = hunk.Path,
            hunk = hunk.Ordinal,
            changedLines = new { total = hunk.Changes.Count, shown = shown[hunk.Order].Count },
            lines = shown[hunk.Order].Select(Line).ToArray(),
        }).ToArray(),
    };

    private static Dictionary<string, object?> Line(Shown line)
    {
        var result = new Dictionary<string, object?>
        {
            ["side"] = line.Change.Side == '+' ? "added" : "removed",
            ["text"] = line.Text,
            ["shortened"] = line.Shortened,
        };
        if (line.Shortened)
        {
            result["originalBytes"] = line.OriginalBytes;
        }

        if (line.Change.NoNewlineAtEnd)
        {
            result["noNewlineAtEnd"] = true;
        }

        return result;
    }

    private static int Serialized(object section) => Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(section));
}
