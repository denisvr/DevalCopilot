namespace DevalCopilot.Application.Features.Runs.Ports;

/// <summary>
/// Invokes an already-resolved, already-revalidated Codex launch target in bounded, read-only,
/// non-interactive implementation-review mode. Provider-specific command construction stays
/// entirely in the Infrastructure implementation, which reuses the same shared bounded Codex
/// process machinery <c>CodexPlanningAdapter</c> and <c>CodexChallengeResolutionAdapter</c> use —
/// never a second, copied invocation contract, and never a new CLI flag. This port speaks only in
/// project-owned terms — a workspace, a sealed manifest to feed as stdin, bounded limits, and a
/// closed outcome. It never resolves or searches for the launch target itself, and it never grants
/// this role any Git, process, network, or repository-mutation capability: a code review never
/// changes the worktree.
/// </summary>
public interface ICodexImplementationReviewAdapter
{
    Task<ImplementationReviewInvocationResult> InvokeAsync(ImplementationReviewInvocationRequest request, CancellationToken cancellationToken);
}
