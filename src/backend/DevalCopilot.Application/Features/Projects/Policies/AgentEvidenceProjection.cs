using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Policies;

namespace DevalCopilot.Application.Features.Projects.Policies;

/// <summary>
/// The delivery projection of a capture for NEW Agent context (ADR-0021). The two root instruction names are reserved to the
/// controlled instruction section: their text is never delivered as a generic untracked preview or as tracked diff, hunk or
/// sample content, whatever the section says about them (Complete, Omitted for any reason, or omitted for budget). Changed paths
/// are untouched; the manifest states the withholding with fixed accounting and never presents the filtered evidence as
/// complete. The projection is structural: untracked entries are replaced by path, and the diff is cut at <c>diff --git</c> file
/// boundaries (which cannot occur inside a hunk) by the path in each block's own header, never by searching repository-controlled
/// text. It applies only to this delivery: ordinary captures, the checkpoint fingerprint and every historical sealed manifest are
/// unchanged, and the raw observation behind the fingerprint is not claimed to have the instruction reader's containment proof.
/// </summary>
public static class AgentEvidenceProjection
{
    private const string FileMarker = "diff --git ";

    /// <summary>The same capture with the reserved paths' generic untracked preview and tracked diff content withheld. It is
    /// idempotent, and the fingerprint, head, outcome, changed paths and instruction context are returned as captured.</summary>
    public static GitWorkspaceEvidenceResult Project(GitWorkspaceEvidenceResult result)
    {
        var reservedChanged = result.ChangedPaths.Any(path => GitWorkspaceInstructionContext.IsReservedPath(path.Path));
        return result with
        {
            CompleteDiff = WithholdReservedDiff(result.CompleteDiff, reservedChanged),
            UntrackedFiles = result.UntrackedFiles is null ? null : ProjectUntracked(result.ChangedPaths, result.UntrackedFiles),
        };
    }

    /// <summary>Removes every file block whose own header names a reserved root path. Only a header this host decodes with
    /// certainty (the default <c>a/</c> and <c>b/</c> prefixes and the same path on both sides) can be classified: a diff that does
    /// not begin with a Git file header, or any block whose header cannot be decoded (a repository configured with
    /// <c>diff.noprefix</c> or <c>diff.mnemonicprefix</c>, a rename, a malformed quote), cannot be cut at a certain boundary, so when
    /// a reserved path changed it is withheld whole, including its unrelated blocks. Unknown never means not reserved, and no other
    /// prefix is guessed. The manifest then states that the generic diff text was withheld and delivers none.</summary>
    internal static string? WithholdReservedDiff(string? diff, bool reservedPathChanged)
    {
        if (string.IsNullOrEmpty(diff) || !reservedPathChanged)
        {
            return diff;
        }

        if (!diff.StartsWith(FileMarker, StringComparison.Ordinal))
        {
            return null;
        }

        var kept = new System.Text.StringBuilder(diff.Length);
        var keepBlock = true;
        var start = 0;
        while (start < diff.Length)
        {
            var newline = diff.IndexOf('\n', start);
            var end = newline < 0 ? diff.Length : newline + 1;
            if (string.CompareOrdinal(diff, start, FileMarker, 0, FileMarker.Length) == 0)
            {
                var header = diff[(start + FileMarker.Length)..(newline < 0 ? diff.Length : newline)];
                var path = TrackedDiffParser.TryParsePath(header);
                if (path is null)
                {
                    return null;
                }

                keepBlock = !GitWorkspaceInstructionContext.IsReservedPath(path);
            }

            if (keepBlock)
            {
                kept.Append(diff, start, end - start);
            }

            start = end;
        }

        return kept.ToString();
    }

    private static IReadOnlyList<GitWorkspaceUntrackedFile> ProjectUntracked(
        IReadOnlyList<GitWorkspaceChangedPath> changedPaths, IReadOnlyList<GitWorkspaceUntrackedFile> untrackedFiles)
    {
        var reservedUntracked = changedPaths
            .Where(path => path.IndexStatus == "?" && path.WorkTreeStatus == "?" && GitWorkspaceInstructionContext.IsReservedPath(path.Path))
            .Select(path => path.Path);
        return untrackedFiles
            .Where(file => !GitWorkspaceInstructionContext.IsReservedPath(file.Path))
            .Concat(reservedUntracked.Select(path =>
                new GitWorkspaceUntrackedFile(path, GitWorkspaceUntrackedOmission.ReservedInstructionFile, null, null, false)))
            .OrderBy(file => file.Path, StringComparer.Ordinal)
            .ToArray();
    }
}
