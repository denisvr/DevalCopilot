using System.Text;
using DevalCopilot.Application.Features.Projects.Ports;

namespace DevalCopilot.Application.Features.Runs;

/// <summary>
/// The one place the five Agent context manifests that carry Git <c>changeEvidence</c> represent
/// untracked (<c>??</c>) files. Every untracked path from the same capture is accounted for exactly once,
/// in ordinal path order: either with a bounded, verified text preview or with an explicit omission
/// reason. A shortened preview says <c>contentComplete: false</c>; a prefix is never presented as a
/// complete file, and the section never claims to cover every change. When no path is untracked the
/// manifest is byte-identical to the one built before this section existed. The whole manifest is
/// measured as serialized: the preview budget is halved until it fits the manifest ceiling, and a final
/// per-file-free summary states that everything was omitted, so a preview never makes an otherwise
/// fitting manifest exceed the bound by more than that marker (roughly 100 bytes; the claim handler's own
/// refusal still applies if even that cannot fit). Everything in the section is
/// repository content and therefore sits under each manifest's untrusted-evidence boundary.
/// </summary>
internal static class UntrackedFileManifestSection
{
    /// <summary>Equals each claim handler's own manifest bound, which still refuses a manifest above it.</summary>
    internal const int ManifestCeilingBytes = 32 * 1024;

    private static readonly int[] TextBudgetsBytes = [16 * 1024, 8 * 1024, 4 * 1024, 2 * 1024, 1024, 0];

    internal const string Notice =
        "Untracked files are listed with a bounded text preview only when the file was read safely and matched " +
        "the captured content identity. A file whose contentComplete is false, or that has no text, was not " +
        "fully provided: never treat a prefix or an omission as the complete file. This list does not prove " +
        "that nothing else changed.";

    private sealed record Entry(string Path, string? OmissionReason, long? SizeBytes, string? Text, bool ContentComplete);

    /// <summary>Serializes with the largest untracked-file section that keeps the manifest within the
    /// ceiling. With no untracked path the manifest is serialized once, without the section.</summary>
    internal static string Fit(
        IReadOnlyList<GitWorkspaceChangedPath> changedPaths,
        IReadOnlyList<GitWorkspaceUntrackedFile>? untrackedFiles,
        Func<object?, string> serialize)
    {
        var entries = Resolve(changedPaths, untrackedFiles);
        if (entries.Count == 0)
        {
            return serialize(null);
        }

        foreach (var budget in TextBudgetsBytes)
        {
            var manifest = serialize(Section(entries, budget));
            if (Encoding.UTF8.GetByteCount(manifest) <= ManifestCeilingBytes)
            {
                return manifest;
            }
        }

        return serialize(new
        {
            allFilesComplete = false,
            omittedFileCount = entries.Count,
            omissionReason = "manifest_budget",
            files = Array.Empty<object>(),
        });
    }

    /// <summary>The <c>changeEvidence</c> object shared by the five manifests. Without a section its
    /// members and order are exactly the historical ones.</summary>
    internal static Dictionary<string, object?> BuildChangeEvidence(
        IReadOnlyList<GitWorkspaceChangedPath> changedPaths, string? completeDiff, int maxDiffCharacters, object? section)
    {
        var evidence = new Dictionary<string, object?>
        {
            ["changedPaths"] = changedPaths
                .Select(path => new { path.Path, path.PreviousPath, path.IndexStatus, path.WorkTreeStatus })
                .ToArray(),
            ["diff"] = completeDiff is null
                ? null
                : completeDiff.Length > maxDiffCharacters ? completeDiff[..maxDiffCharacters] : completeDiff,
            ["diffTruncated"] = completeDiff is not null && completeDiff.Length > maxDiffCharacters,
        };

        if (section is not null)
        {
            evidence["untrackedFiles"] = section;
        }

        return evidence;
    }

    private static List<Entry> Resolve(
        IReadOnlyList<GitWorkspaceChangedPath> changedPaths, IReadOnlyList<GitWorkspaceUntrackedFile>? untrackedFiles)
    {
        var byPath = new Dictionary<string, GitWorkspaceUntrackedFile>(StringComparer.Ordinal);
        foreach (var file in untrackedFiles ?? [])
        {
            byPath.TryAdd(file.Path, file);
        }

        return changedPaths
            .Where(path => path.IndexStatus == "?" && path.WorkTreeStatus == "?")
            .Select(path => path.Path)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => byPath.TryGetValue(path, out var file)
                ? new Entry(path, file.Omission is { } omission ? Reason(omission) : null, file.SizeBytes, file.Text, file.ContentComplete)
                : new Entry(path, "not_captured", null, null, false))
            .ToList();
    }

    private static object Section(IReadOnlyList<Entry> entries, int textBudgetBytes)
    {
        var used = 0;
        var allComplete = true;
        var files = new List<object>(entries.Count);
        foreach (var entry in entries)
        {
            var reason = entry.OmissionReason;
            string? text = null;
            var complete = false;

            if (reason is null)
            {
                var bytes = Encoding.UTF8.GetBytes(entry.Text ?? string.Empty);
                var cut = Math.Min(bytes.Length, textBudgetBytes - used);
                while (cut > 0 && cut < bytes.Length && (bytes[cut] & 0xC0) == 0x80)
                {
                    cut--;
                }

                if (cut <= 0 && bytes.Length > 0)
                {
                    reason = "manifest_budget";
                }
                else
                {
                    text = Encoding.UTF8.GetString(bytes, 0, cut);
                    complete = entry.ContentComplete && cut == bytes.Length;
                    used += cut;
                }
            }

            allComplete &= complete;
            files.Add(new
            {
                path = entry.Path,
                preview = reason is null ? "included" : "omitted",
                omissionReason = reason,
                sizeBytes = entry.SizeBytes,
                contentComplete = complete,
                text,
            });
        }

        return new
        {
            notice = Notice,
            allFilesComplete = allComplete,
            files,
        };
    }

    private static string Reason(GitWorkspaceUntrackedOmission omission) => omission switch
    {
        GitWorkspaceUntrackedOmission.NotRegularFile => "not_regular_file",
        GitWorkspaceUntrackedOmission.ContainmentUnproven => "containment_unproven",
        GitWorkspaceUntrackedOmission.Missing => "missing",
        GitWorkspaceUntrackedOmission.Unreadable => "unreadable",
        GitWorkspaceUntrackedOmission.ContentIdentityMismatch => "content_identity_mismatch",
        GitWorkspaceUntrackedOmission.TooLarge => "too_large",
        GitWorkspaceUntrackedOmission.Binary => "binary",
        GitWorkspaceUntrackedOmission.InvalidUtf8 => "invalid_utf8",
        GitWorkspaceUntrackedOmission.AggregateLimit => "aggregate_limit",
        _ => "unverifiable",
    };
}
