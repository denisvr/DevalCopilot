namespace DevalCopilot.Application.Features.Runs.Ports;

/// <summary>What an exact, read-only look at Git and the filesystem proves about one recorded operation. Facts, not decisions.
/// <paramref name="SourceConsistent"/> is the post-observation answer: the working files equal the recorded tree.
/// <paramref name="ReferenceLocks"/> is the bounded proof about the owned reference and HEAD locks; it defaults to
/// <see cref="LocalCommitReferenceLockState.Unproven"/>, so only an inspection that actually observed a clear namespace can carry
/// <see cref="LocalCommitReferenceLockState.Clear"/>.</summary>
public sealed record LocalCommitInspection(
    LocalCommitInspectionOutcome Outcome,
    bool OwnershipProven,
    bool HeadBoundToBranch,
    string? BranchTipSha,
    LocalCommitObjectState CommitObject,
    LocalCommitIndexState Index,
    LocalCommitLockState IndexLock,
    bool PreparedArtifactIntact,
    bool SourceConsistent,
    LocalCommitReferenceLockState ReferenceLocks = LocalCommitReferenceLockState.Unproven);
