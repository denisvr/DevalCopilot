using System.Text;
using DevalCopilot.Application.Features.Projects.Policies;
using DevalCopilot.Application.Features.Projects.Ports;

namespace DevalCopilot.Application.Features.Runs.Policies;

/// <summary>
/// The one place the five Agent context manifests that carry Git evidence build <c>changeEvidence</c>: every
/// changed path, a bounded tracked-diff representation, and the untracked-file section. A tracked diff that is
/// text-only, recognized, and within <see cref="MaxInlinedDiffBytes"/> UTF-8 bytes is inlined exactly (the
/// historical shape, <c>diffTruncated: false</c>). Otherwise complete file headers and complete hunks are selected
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

    internal const string ReservedDiffWithheld = "reserved_instruction_diff_withheld";

    internal const string ReservedDiffWithheldNotice =
        "No tracked diff text is delivered: a tracked root instruction file changed and the diff could not be cut at certain file " +
        "boundaries, so the whole generic diff, including unrelated hunks, was withheld. Every changed file is listed in changedPaths.";

    internal const string DiffNotice =
        "The tracked diff below is a bounded selection of complete file headers and hunks, not the full diff and not " +
        "an applyable patch. Every file that is partial or absent is listed here with a fixed reason.";

    /// <summary>Serializes the manifest around the largest <c>changeEvidence</c> that keeps it within the ceiling.</summary>
    internal static string Fit(
        IReadOnlyList<GitWorkspaceChangedPath> changedPaths,
        string? completeDiff,
        IReadOnlyList<GitWorkspaceUntrackedFile>? untrackedFiles,
        Func<Dictionary<string, object?>, string> serialize)
    {
        // The two root instruction names are reserved to the instruction section: their tracked content is withheld here whatever
        // their instruction status (and whatever a capture already projected), and the withholding is always stated.
        var reserved = changedPaths
            .Where(path => GitWorkspaceInstructionContext.IsReservedPath(path.Path))
            .Select(path => path.Path)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var trackedReservedChanged = changedPaths.Any(path =>
            GitWorkspaceInstructionContext.IsReservedPath(path.Path) && !(path.IndexStatus == "?" && path.WorkTreeStatus == "?"));
        completeDiff = AgentEvidenceProjection.WithholdReservedDiff(completeDiff, reserved.Length > 0);
        var parsed = completeDiff is null ? null : TrackedDiffParser.Parse(completeDiff);
        var entries = UntrackedFileManifestSection.Resolve(changedPaths, untrackedFiles);

        foreach (var (diffBytes, untrackedBytes, sampleBytes) in Steps)
        {
            var manifest = serialize(Evidence(
                changedPaths, completeDiff, parsed, diffBytes, sampleBytes, includeItems: true, reserved, trackedReservedChanged,
                entries.Count == 0 ? null : UntrackedFileManifestSection.Section(entries, untrackedBytes)));
            if (Fits(manifest))
            {
                return manifest;
            }
        }

        var withoutItems = serialize(Evidence(
            changedPaths, completeDiff, parsed, 0, 0, includeItems: false, reserved, trackedReservedChanged,
            entries.Count == 0 ? null : UntrackedFileManifestSection.Section(entries, 0)));
        if (Fits(withoutItems))
        {
            return withoutItems;
        }

        return serialize(Evidence(
            changedPaths, completeDiff, parsed, 0, 0, includeItems: false, reserved, trackedReservedChanged,
            entries.Count == 0 ? null : UntrackedFileManifestSection.Summary(entries)));
    }

    private static bool Fits(string manifest) => Encoding.UTF8.GetByteCount(manifest) <= ManifestCeilingBytes;

    private static Dictionary<string, object?> Evidence(
        IReadOnlyList<GitWorkspaceChangedPath> changedPaths,
        string? completeDiff,
        ParsedTrackedDiff? parsed,
        int diffBudgetBytes,
        int sampleBudgetBytes,
        bool includeItems,
        IReadOnlyList<string> reservedInstructionFiles,
        bool trackedReservedChanged,
        object? untrackedSection)
    {
        var evidence = new Dictionary<string, object?>
        {
            ["changedPaths"] = changedPaths
                .Select(path => new { path.Path, path.PreviousPath, path.IndexStatus, path.WorkTreeStatus })
                .ToArray(),
        };

        if (completeDiff is null || parsed is null)
        {
            evidence["diff"] = null;
            evidence["diffTruncated"] = false;
        }
        else if (IsExactlyInlinable(completeDiff, parsed, diffBudgetBytes))
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

        if (reservedInstructionFiles.Count > 0)
        {
            // Never complete evidence: the tracked content of these root instruction files is delivered only by the instruction
            // section. The accounting survives every reduction step because it is a few fixed bytes.
            var selection = evidence.TryGetValue("diffSelection", out var existing) && existing is Dictionary<string, object?> current
                ? current
                : new Dictionary<string, object?> { ["notice"] = DiffNotice, ["complete"] = false };
            selection["reservedInstructionFiles"] = reservedInstructionFiles;
            if (trackedReservedChanged && evidence["diff"] is null && !selection.ContainsKey("reason"))
            {
                // No diff text at all although a tracked root instruction file changed: the whole generic diff is withheld (it could not
                // be cut at a certain boundary), which also withholds unrelated hunks. The changed paths above still name every file.
                selection["reason"] = ReservedDiffWithheld;
                selection["notice"] = ReservedDiffWithheldNotice;
            }
            evidence["diffTruncated"] = true;
            evidence["diffSelection"] = selection;
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
        && (completeDiff.Length == 0
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
