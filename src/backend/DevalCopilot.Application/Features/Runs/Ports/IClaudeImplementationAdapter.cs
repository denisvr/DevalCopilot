namespace DevalCopilot.Application.Features.Runs.Ports;

/// <summary>
/// Invokes an already-resolved, already-revalidated Claude launch target in bounded,
/// non-interactive implementation mode, confined to the owned worktree with no shell, web,
/// browser, MCP, or arbitrary process capability. Provider-specific command construction stays
/// entirely in the Infrastructure implementation — a dedicated adapter, never the critical-review
/// adapter reused by changing flags, since this role's tool allowlist and permission mode differ
/// in kind (mutating repository edits) from every other Claude usage in this protocol. This port
/// speaks only in project-owned terms — a workspace, a sealed manifest to feed as stdin, bounded
/// limits, and a closed outcome. It never resolves or searches for the launch target itself, and
/// it never runs Git, verification, or network commands on Claude's behalf.
/// </summary>
public interface IClaudeImplementationAdapter
{
    Task<ImplementationInvocationResult> InvokeAsync(ImplementationInvocationRequest request, CancellationToken cancellationToken);
}
