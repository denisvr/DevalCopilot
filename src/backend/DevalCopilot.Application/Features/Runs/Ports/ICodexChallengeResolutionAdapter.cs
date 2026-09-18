namespace DevalCopilot.Application.Features.Runs.Ports;

/// <summary>
/// Invokes an already-resolved, already-revalidated Codex launch target in bounded, read-only,
/// non-interactive challenge-resolution mode. Provider-specific command construction stays
/// entirely in the Infrastructure implementation, which reuses the same shared bounded Codex
/// process machinery <c>CodexPlanningAdapter</c> uses — never a second, copied invocation
/// contract. This port speaks only in project-owned terms — a workspace, a sealed manifest to
/// feed as stdin, bounded limits, and a closed outcome. It never resolves or searches for the
/// launch target itself.
/// </summary>
public interface ICodexChallengeResolutionAdapter
{
    Task<ChallengeResolutionInvocationResult> InvokeAsync(ChallengeResolutionInvocationRequest request, CancellationToken cancellationToken);
}
