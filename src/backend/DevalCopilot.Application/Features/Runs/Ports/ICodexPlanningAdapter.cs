namespace DevalCopilot.Application.Features.Runs.Ports;

/// <summary>
/// Invokes a already-resolved, already-revalidated Codex launch target in bounded, read-only,
/// non-interactive planning mode. Provider-specific command construction (the exact CLI contract)
/// stays entirely in the Infrastructure implementation; this port speaks only in project-owned
/// terms — a workspace, a sealed manifest to feed as stdin, bounded limits, and a closed outcome.
/// It never resolves or searches for the launch target itself.
/// </summary>
public interface ICodexPlanningAdapter
{
    Task<CodexPlanningInvocationResult> InvokeAsync(CodexPlanningInvocationRequest request, CancellationToken cancellationToken);
}
