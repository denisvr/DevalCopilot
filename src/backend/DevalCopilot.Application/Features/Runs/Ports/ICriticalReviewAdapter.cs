namespace DevalCopilot.Application.Features.Runs.Ports;

/// <summary>
/// Invokes an already-resolved, already-revalidated Claude Code launch target in bounded,
/// fail-closed, read-only, non-interactive critical-review mode. Provider-specific command
/// construction (the exact CLI contract) stays entirely in the Infrastructure implementation;
/// this port speaks only in project-owned terms — a workspace, a sealed manifest to feed as
/// stdin, bounded limits, and a closed outcome. It never resolves or searches for the launch
/// target itself, and it never sends the reviewed Proposal or any repository content anywhere
/// outside the manifest already sealed by the caller.
/// </summary>
public interface ICriticalReviewAdapter
{
    Task<CriticalReviewInvocationResult> InvokeAsync(CriticalReviewInvocationRequest request, CancellationToken cancellationToken);
}
