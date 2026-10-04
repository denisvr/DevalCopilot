using System.Text;
using DevalCopilot.Application.Features.Projects.Ports;

namespace DevalCopilot.Application.Features.Runs.Policies;

/// <summary>
/// How the Agent context manifests represent untracked (<c>??</c>) files inside <c>changeEvidence</c>. Every
/// untracked path from the same capture is accounted for exactly once, in ordinal path order: either with a
/// bounded, verified text preview or with an explicit omission reason. A shortened preview says
/// <c>contentComplete: false</c>; a prefix is never presented as a complete file, and the section never claims to
/// cover every change. <see cref="ChangeEvidenceManifest"/> decides how much preview text fits the manifest
/// ceiling next to the tracked-diff selection. Everything here is repository content and therefore sits under each
/// manifest's untrusted-evidence boundary.
/// </summary>
internal static class UntrackedFileManifestSection
{
    internal const string Notice =
        "Untracked files are listed with a bounded text preview only when the file was read safely and matched " +
        "the captured content identity. A file whose contentComplete is false, or that has no text, was not " +
        "fully provided: never treat a prefix or an omission as the complete file. This list does not prove " +
        "that nothing else changed.";

    internal sealed record Entry(string Path, string? OmissionReason, long? SizeBytes, string? Text, bool ContentComplete);

    internal static List<Entry> Resolve(
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
            .Select(path => GitWorkspaceInstructionContext.IsReservedPath(path)
                ? new Entry(path, Reason(GitWorkspaceUntrackedOmission.ReservedInstructionFile), null, null, false)
                : byPath.TryGetValue(path, out var file)
                ? new Entry(path, file.Omission is { } omission ? Reason(omission) : null, file.SizeBytes, file.Text, file.ContentComplete)
                : new Entry(path, "not_captured", null, null, false))
            .ToList();
    }

    internal static object Section(IReadOnlyList<Entry> entries, int textBudgetBytes)
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

    /// <summary>The last, per-file-free form: it still states that every untracked file was omitted and how many.</summary>
    internal static object Summary(IReadOnlyList<Entry> entries) => new
    {
        allFilesComplete = false,
        omittedFileCount = entries.Count,
        omissionReason = "manifest_budget",
        files = Array.Empty<object>(),
    };

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
        GitWorkspaceUntrackedOmission.ReservedInstructionFile => "reserved_instruction_file",
        _ => "unverifiable",
    };
}
