using DevalCopilot.Domain.Features.Projects;

namespace DevalCopilot.Application.Features.Projects.Ports;

/// <summary>
/// Answers only Git-specific questions about an already filesystem-validated candidate root —
/// composed entirely on top of the existing <c>IProcessExecutionAdapter</c> boundary, the same
/// way <c>ToolDiscoveryAdapter</c> is, never a second child-process execution path. Takes a
/// <see cref="RepositoryRootCandidate"/>, not a raw string, so a caller cannot invoke Git
/// inspection against a path that has not already passed filesystem validation.
/// </summary>
public interface IGitRepositoryInspector
{
    Task<GitRepositoryInspectionResult> InspectAsync(
        RepositoryRootCandidate candidate, CancellationToken cancellationToken);
}

public enum GitRepositoryInspectionOutcome
{
    Success,

    /// <summary>The <c>git</c> executable could not be resolved or invoked at all.</summary>
    GitUnavailable,

    /// <summary>The candidate root is not inside any Git repository.</summary>
    NotAGitRepository,

    /// <summary>The candidate root is a bare repository (no working tree) — not supported by
    /// this slice, since dirty/branch/HEAD observation assumes a working tree.</summary>
    BareRepositoryNotSupported,

    /// <summary>The candidate root belongs to a linked Git worktree rather than the main
    /// working tree. Rejected: DevalCopilot owns worktree creation (ADR-0005) and must not
    /// adopt a worktree it did not create as if it were an independent top-level repository.</summary>
    LinkedWorktreeNotSupported,

    /// <summary>The candidate root is inside a Git repository, but is not that repository's own
    /// top-level working-tree root (e.g. a subdirectory, or the <c>.git</c> directory itself).</summary>
    NotTopLevelRoot,

    /// <summary>The bounded before/after HEAD-signature check detected a change to the
    /// repository while it was being inspected, even after one retry. The observation was
    /// discarded rather than persisted as a torn snapshot.</summary>
    RepositoryChangedDuringInspection,

    /// <summary><c>symbolic-ref</c> and <c>rev-parse HEAD</c> did not together match any of the
    /// three valid HEAD combinations (OnBranch, Unborn, Detached) — e.g. both failed, or one
    /// exited with an unexpected code or output shape. Never fabricated as a guessed state.</summary>
    InvalidHeadState,

    /// <summary>One of the fixed, bounded Git invocations exceeded its timeout.</summary>
    GitInvocationTimedOut,
}

public sealed record GitRepositoryInspectionResult(
    GitRepositoryInspectionOutcome Outcome,
    RepositoryHeadState? HeadState,
    string? BranchName,
    string? HeadCommitSha,
    bool IsDirty);
