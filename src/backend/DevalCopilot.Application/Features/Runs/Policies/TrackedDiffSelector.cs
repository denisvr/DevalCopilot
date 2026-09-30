using System.Text;

namespace DevalCopilot.Application.Features.Runs.Policies;

/// <summary>
/// Chooses complete file headers and complete hunks from a parsed tracked diff under a UTF-8 byte budget. Selection
/// proceeds in rounds over the files in diff order and gives each file at most one further hunk per round (its
/// earliest unselected hunk that still fits), so every file gets a first hunk before any file gets a second, and an
/// oversized hunk never prevents a later, smaller hunk of the same or another file from being chosen. Text is only
/// ever assembled from whole hunks, so no hunk and no Unicode scalar is split, and the output keeps the original
/// file and hunk order. The result is deterministic for identical input.
/// </summary>
internal static class TrackedDiffSelector
{
    internal static TrackedDiffSelection Select(ParsedTrackedDiff parsed, int budgetBytes)
    {
        var files = parsed.Files;
        var headerBytes = files.Select(file => Encoding.UTF8.GetByteCount(file.Header)).ToArray();
        var hunkBytes = files.Select(file => file.Hunks.Select(Encoding.UTF8.GetByteCount).ToArray()).ToArray();
        var headerIncluded = new bool[files.Count];
        var selected = files.Select(file => new bool[file.Hunks.Count]).ToArray();
        var remaining = Math.Max(0, budgetBytes);

        bool progress;
        do
        {
            progress = false;
            for (var fileIndex = 0; fileIndex < files.Count; fileIndex++)
            {
                var file = files[fileIndex];
                if (file.Kind == TrackedDiffFileKind.MetadataOnly)
                {
                    if (!headerIncluded[fileIndex] && headerBytes[fileIndex] <= remaining)
                    {
                        headerIncluded[fileIndex] = true;
                        remaining -= headerBytes[fileIndex];
                        progress = true;
                    }

                    continue;
                }

                if (file.Kind != TrackedDiffFileKind.Text)
                {
                    continue;
                }

                for (var hunkIndex = 0; hunkIndex < file.Hunks.Count; hunkIndex++)
                {
                    if (selected[fileIndex][hunkIndex])
                    {
                        continue;
                    }

                    var cost = hunkBytes[fileIndex][hunkIndex] + (headerIncluded[fileIndex] ? 0 : headerBytes[fileIndex]);
                    if (cost > remaining)
                    {
                        continue;
                    }

                    selected[fileIndex][hunkIndex] = true;
                    headerIncluded[fileIndex] = true;
                    remaining -= cost;
                    progress = true;
                    break;
                }
            }
        }
        while (progress);

        return Assemble(files, headerIncluded, selected, hunkBytes, headerBytes, Math.Max(0, budgetBytes));
    }

    private static TrackedDiffSelection Assemble(
        IReadOnlyList<TrackedDiffFile> files,
        bool[] headerIncluded,
        bool[][] selected,
        int[][] hunkBytes,
        int[] headerBytes,
        int budgetBytes)
    {
        var text = new StringBuilder();
        var items = new List<TrackedDiffItem>();
        int includedFiles = 0, partialFiles = 0, omittedFiles = 0, totalHunks = 0, includedHunks = 0;

        for (var fileIndex = 0; fileIndex < files.Count; fileIndex++)
        {
            var file = files[fileIndex];
            var included = selected[fileIndex].Count(flag => flag);
            totalHunks += file.Hunks.Count;
            includedHunks += included;

            if (headerIncluded[fileIndex])
            {
                text.Append(file.Header);
                for (var hunkIndex = 0; hunkIndex < file.Hunks.Count; hunkIndex++)
                {
                    if (selected[fileIndex][hunkIndex])
                    {
                        text.Append(file.Hunks[hunkIndex]);
                    }
                }
            }

            if (file.Kind is TrackedDiffFileKind.Binary or TrackedDiffFileKind.Unsupported)
            {
                omittedFiles++;
                items.Add(new TrackedDiffItem(file.Path, file.Kind, 0, 0, "omitted", file.Reason!));
            }
            else if (included == file.Hunks.Count && (file.Kind == TrackedDiffFileKind.Text || headerIncluded[fileIndex]))
            {
                includedFiles++;
            }
            else
            {
                var tooLarge = Enumerable.Range(0, file.Hunks.Count)
                    .Where(hunkIndex => !selected[fileIndex][hunkIndex])
                    .All(hunkIndex => hunkBytes[fileIndex][hunkIndex] + headerBytes[fileIndex] > budgetBytes);
                var partial = included > 0;
                if (partial)
                {
                    partialFiles++;
                }
                else
                {
                    omittedFiles++;
                }

                items.Add(new TrackedDiffItem(
                    file.Path,
                    file.Kind,
                    file.Hunks.Count,
                    included,
                    partial ? "partial" : "omitted",
                    tooLarge && file.Kind == TrackedDiffFileKind.Text ? "hunk_too_large" : "diff_budget"));
            }
        }

        return new TrackedDiffSelection(
            text.ToString(), items, files.Count, includedFiles, partialFiles, omittedFiles, totalHunks, includedHunks);
    }
}
