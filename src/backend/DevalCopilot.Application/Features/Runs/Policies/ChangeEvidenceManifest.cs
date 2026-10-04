using System.Text;
using DevalCopilot.Application.Features.Projects.Policies;
using DevalCopilot.Application.Features.Projects.Ports;

namespace DevalCopilot.Application.Features.Runs.Policies;

/// <summary>
/// The one place the Agent context manifests that carry Git evidence build <c>changeEvidence</c>: every
/// changed path, a bounded tracked-diff representation, and the untracked-file section. The tracked diff is never a Git patch:
/// it is the host comparison of attested snapshots (<see cref="TrackedChangeEvidence"/>, ADR-0024), and every tracked path
/// without text is accounted for with a fixed reason. A comparison that is text-only, has no omission, and is within
/// <see cref="MaxInlinedDiffBytes"/> UTF-8 bytes is inlined exactly (<c>diffTruncated: false</c>). Otherwise complete file headers and complete hunks are selected
/// (<see cref="TrackedDiffSelector"/>) and <c>diffSelection</c> states, with fixed reasons and counts, everything
/// that is partial or absent; the text is then explicitly not the full or an applyable patch. Valid text hunks too
/// large for any selection contribute a separate, bounded, incomplete changed-line sample
/// (<see cref="TrackedDiffSampler"/>) under <c>diffSelection.samples</c>, never inside <c>diff</c>. The whole manifest is
/// measured as serialized against the ceiling and optional text (tracked hunks and untracked previews together) is
/// reduced step by step; the accounting for tracked and untracked evidence is reduced only to its counts-only form,
/// never dropped. If even that cannot fit, the last form is returned and each claim handler's own refusal applies.
/// Everything here is repository content and sits under each manifest's untrusted-evidence boundary. The project instruction
/// section is fitted around this ladder by <see cref="ProjectInstructionContextManifest.Fit"/>: whole instruction texts are
/// omitted only after every reduction here has been exhausted.
/// </summary>
internal static class ChangeEvidenceManifest
{
    /// <summary>Equals each claim handler's own manifest bound, which still refuses a manifest above it.</summary>
    internal const int ManifestCeilingBytes = 32 * 1024;

    internal const int MaxInlinedDiffBytes = 8 * 1024;

    private static readonly (int DiffBytes, int UntrackedBytes, int SampleBytes)[] Steps =
    [
        (MaxInlinedDiffBytes, 16 * 1024, TrackedDiffSampler.MaxSectionBytes),
        (MaxInlinedDiffBytes, 8 * 1024, TrackedDiffSampler.MaxSectionBytes),
        (4 * 1024, 4 * 1024, 2 * 1024),
        (2 * 1024, 2 * 1024, 1024),
        (1024, 1024, 0),
        (0, 0, 0),
    ];

    internal const string DiffNotice =
        "The tracked diff below is a bounded selection of complete file headers and hunks, not the full diff and not " +
        "an applyable patch. Every file that is partial or absent is listed here with a fixed reason.";

    internal const string ComparisonMethod = "host_prefix_suffix_v1";

    /// <summary>The same statement in its shortest form, used only when the manifest has been reduced to its counts-only accounting.</summary>
    internal const string CompactComparisonNotice =
        "Host comparison of attested snapshots, not Git's minimal or filter-normalized patch: unchanged middle lines can appear " +
        "changed and line-ending-only differences remain visible.";

    internal const string ComparisonNotice =
        "Tracked changes are compared by this host from attested snapshots of the captured HEAD blob and the proven current file, " +
        "never taken from Git's patch. Each file is one replacement hunk with at most three unchanged context lines at each edge: " +
        "this is not Git's minimal or filter-normalized patch, so unchanged lines inside the replaced middle can appear as removed " +
        "and added, and line-ending-only differences remain visible. File modes and renames are not compared. Every tracked path " +
        "without text is listed with a fixed reason.";

    /// <summary>Serializes the manifest around the largest <c>changeEvidence</c> that keeps it within the ceiling.</summary>
    internal static string Fit(
        IReadOnlyList<GitWorkspaceChangedPath> changedPaths,
        TrackedChangeEvidence tracked,
        IReadOnlyList<GitWorkspaceUntrackedFile>? untrackedFiles,
        Func<Dictionary<string, object?>, string> serialize)
    {
        // The two root instruction names are reserved to the instruction section: their tracked content is an omission here
        // whatever their instruction status, and the withholding is always stated.
        var reserved = changedPaths
            .Where(path => GitWorkspaceInstructionContext.IsReservedPath(path.Path))
            .Select(path => path.Path)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var hasTrackedPaths = changedPaths.Any(path => !(path.IndexStatus == "?" && path.WorkTreeStatus == "?"));
        var parsed = Parse(tracked);
        var entries = UntrackedFileManifestSection.Resolve(changedPaths, untrackedFiles);

        foreach (var (diffBytes, untrackedBytes, sampleBytes) in Steps)
        {
            var manifest = serialize(Evidence(
                changedPaths, tracked, parsed, diffBytes, sampleBytes, includeItems: true, reserved, hasTrackedPaths,
                entries.Count == 0 ? null : UntrackedFileManifestSection.Section(entries, untrackedBytes)));
            if (Fits(manifest))
            {
                return manifest;
            }
        }

        var withoutItems = serialize(Evidence(
            changedPaths, tracked, parsed, 0, 0, includeItems: false, reserved, hasTrackedPaths,
            entries.Count == 0 ? null : UntrackedFileManifestSection.Section(entries, 0)));
        if (Fits(withoutItems))
        {
            return withoutItems;
        }

        return serialize(Evidence(
            changedPaths, tracked, parsed, 0, 0, includeItems: false, reserved, hasTrackedPaths,
            entries.Count == 0 ? null : UntrackedFileManifestSection.Summary(entries)));
    }

    private static bool Fits(string manifest) => Encoding.UTF8.GetByteCount(manifest) <= ManifestCeilingBytes;

    /// <summary>The comparison text parsed into files, with every omitted path merged in as one omitted file in ordinal path order,
    /// or null when no tracked evidence was provided at all.</summary>
    private static ParsedTrackedDiff? Parse(TrackedChangeEvidence tracked)
    {
        if (tracked.Text is null && tracked.Omissions.Count == 0)
        {
            return null;
        }

        var parsed = TrackedDiffParser.Parse(tracked.Text ?? string.Empty);
        if (tracked.Omissions.Count == 0 || !parsed.Recognized)
        {
            return parsed;
        }

        var files = parsed.Files
            .Concat(tracked.Omissions.Select(omission =>
                new TrackedDiffFile(omission.Path, TrackedDiffFileKind.Unsupported, string.Empty, [], omission.Reason)))
            .OrderBy(file => file.Path is null ? 1 : 0)
            .ThenBy(file => file.Path, StringComparer.Ordinal)
            .ToArray();
        return parsed with { Files = files };
    }

    private static Dictionary<string, object?> Evidence(
        IReadOnlyList<GitWorkspaceChangedPath> changedPaths,
        TrackedChangeEvidence tracked,
        ParsedTrackedDiff? parsed,
        int diffBudgetBytes,
        int sampleBudgetBytes,
        bool includeItems,
        IReadOnlyList<string> reservedInstructionFiles,
        bool hasTrackedPaths,
        object? untrackedSection)
    {
        var evidence = new Dictionary<string, object?>
        {
            ["changedPaths"] = changedPaths
                .Select(path => new { path.Path, path.PreviousPath, path.IndexStatus, path.WorkTreeStatus })
                .ToArray(),
        };

        var completeDiff = tracked.Text;
        if (parsed is null)
        {
            evidence["diff"] = null;
            evidence["diffTruncated"] = false;
        }
        else if (IsExactlyInlinable(completeDiff ?? string.Empty, parsed, diffBudgetBytes))
        {
            evidence["diff"] = completeDiff;
            evidence["diffTruncated"] = false;
        }
        else if (!parsed.Recognized)
        {
            evidence["diff"] = null;
            evidence["diffTruncated"] = true;
            evidence["diffSelection"] = new Dictionary<string, object?>
            {
                ["notice"] = DiffNotice,
                ["complete"] = false,
                ["reason"] = "unsupported_format",
            };
        }
        else
        {
            var selection = TrackedDiffSelector.Select(parsed, diffBudgetBytes);
            evidence["diff"] = selection.Text;
            evidence["diffTruncated"] = true;
            evidence["diffSelection"] = DiffSelection(
                selection, includeItems, diffBudgetBytes == 0 && !includeItems, TrackedDiffSampler.Build(parsed, sampleBudgetBytes));
        }

        if (reservedInstructionFiles.Count > 0 || tracked.Omissions.Count > 0)
        {
            // Never complete evidence: reserved root instruction files are delivered only by the instruction section, and every
            // omitted tracked path is counted by fixed reason. This accounting survives every reduction step because it is a few
            // fixed bytes, even when the per-file items are dropped.
            var selection = evidence.TryGetValue("diffSelection", out var existing) && existing is Dictionary<string, object?> current
                ? current
                : new Dictionary<string, object?> { ["notice"] = DiffNotice, ["complete"] = false };
            if (reservedInstructionFiles.Count > 0)
            {
                selection["reservedInstructionFiles"] = reservedInstructionFiles;
            }

            if (tracked.Omissions.Count > 0)
            {
                selection["omissionReasons"] = tracked.Omissions
                    .GroupBy(omission => omission.Reason, StringComparer.Ordinal)
                    .OrderBy(group => group.Key, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
            }

            evidence["diffTruncated"] = true;
            evidence["diffSelection"] = selection;
        }

        if (hasTrackedPaths)
        {
            evidence["trackedComparison"] = new { method = ComparisonMethod, notice = includeItems ? ComparisonNotice : CompactComparisonNotice };
        }

        if (untrackedSection is not null)
        {
            evidence["untrackedFiles"] = untrackedSection;
        }

        return evidence;
    }

    /// <summary>The exact string may be inlined as complete only for an empty diff or a recognized parse in which every
    /// file is a supported text or metadata-only block, with no binary patch, that fits the budget.</summary>
    private static bool IsExactlyInlinable(string completeDiff, ParsedTrackedDiff parsed, int diffBudgetBytes) =>
        Encoding.UTF8.GetByteCount(completeDiff) <= diffBudgetBytes
        && ((completeDiff.Length == 0 && parsed.Files.Count == 0)
            || (parsed.Recognized
                && !parsed.ContainsBinaryPatch
                && parsed.Files.All(file => file.Kind is TrackedDiffFileKind.Text or TrackedDiffFileKind.MetadataOnly)));

    private static Dictionary<string, object?> DiffSelection(
        TrackedDiffSelection selection, bool includeItems, bool minimal, object? samples)
    {
        var result = new Dictionary<string, object?>();
        if (!minimal)
        {
            result["notice"] = DiffNotice;
        }

        result["complete"] = false;
        result["files"] = new
        {
            total = selection.TotalFiles,
            included = selection.IncludedFiles,
            partial = selection.PartialFiles,
            omitted = selection.OmittedFiles,
        };
        result["hunks"] = new { total = selection.TotalHunks, included = selection.IncludedHunks };
        if (includeItems)
        {
            result["items"] = selection.Items
                .Select(item => new
                {
                    path = item.Path,
                    kind = Kind(item.Kind),
                    selection = item.Selection,
                    hunks = new { total = item.TotalHunks, included = item.IncludedHunks },
                    reason = item.Reason,
                })
                .ToArray();
        }
        else
        {
            result["itemsOmitted"] = true;
            result["omissionReason"] = "manifest_budget";
        }

        if (samples is not null)
        {
            result["samples"] = samples;
        }

        return result;
    }

    private static string Kind(TrackedDiffFileKind kind) => kind switch
    {
        TrackedDiffFileKind.Text => "text",
        TrackedDiffFileKind.MetadataOnly => "metadata_only",
        TrackedDiffFileKind.Binary => "binary",
        _ => "unsupported",
    };
}
