using DevalCopilot.Application.Features.Projects.Ports;

namespace DevalCopilot.Application.Features.Projects.Policies;

/// <summary>
/// The delivery projection of a capture for checkpoint inspection by a human (ADR-0027). Like <see cref="AgentEvidenceProjection"/>
/// it removes the raw working-path patch, because that patch is produced by Git reading the repository pathnames and can contain
/// the bytes of a file any other name reaches. Unlike it, nothing is reserved: a physically proven tracked root instruction file is
/// ordinary displayed source text here, and no instruction context or untracked preview is ever part of an inspection capture.
/// The changed paths, the fingerprint, the head and the attested tracked facts are returned as captured.
/// </summary>
public static class CheckpointInspectionProjection
{
    /// <summary>The same capture without the raw patch, instruction context or untracked previews. It is idempotent.</summary>
    public static GitWorkspaceEvidenceResult Project(GitWorkspaceEvidenceResult result) =>
        result with { CompleteDiff = null, UntrackedFiles = null, InstructionContext = null };
}
