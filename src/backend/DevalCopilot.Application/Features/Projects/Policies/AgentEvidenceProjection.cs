using DevalCopilot.Application.Features.Projects.Ports;

namespace DevalCopilot.Application.Features.Projects.Policies;

/// <summary>
/// The delivery projection of a capture for NEW Agent context (ADR-0021, ADR-0024). It applies to this delivery only:
/// ordinary captures, the checkpoint fingerprint and every historical sealed manifest are unchanged, and the raw observation
/// behind the fingerprint is not claimed to have the readers' containment proof.
/// <list type="bullet">
/// <item>The two root instruction names are reserved to the controlled instruction section: their text is never delivered as a
/// generic untracked preview (the untracked entry is replaced by a path with a fixed reason) or as tracked evidence.</item>
/// <item>The raw working-path patch is never part of the delivered capture: <see cref="GitWorkspaceEvidenceResult.CompleteDiff"/> is
/// removed, because that patch is produced by Git reading the repository pathnames and can contain the bytes of a file any
/// other name reaches. The tracked evidence is the attested <see cref="GitWorkspaceEvidenceResult.TrackedFiles"/> instead.</item>
/// </list>
/// Changed paths, the fingerprint, the head and the instruction context are returned as captured.
/// </summary>
public static class AgentEvidenceProjection
{
    /// <summary>The same capture with the raw patch removed and the reserved paths' generic untracked preview withheld. It is
    /// idempotent.</summary>
    public static GitWorkspaceEvidenceResult Project(GitWorkspaceEvidenceResult result) =>
        result with
        {
            CompleteDiff = null,
            UntrackedFiles = result.UntrackedFiles is null ? null : ProjectUntracked(result.ChangedPaths, result.UntrackedFiles),
        };

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
