namespace DevalCopilot.Application.Features.Runs.Ports;

/// <summary>
/// Invokes an already-resolved, already-revalidated Codex launch target in bounded, read-only, non-interactive
/// verification-diagnosis mode (ADR-0018). Provider-specific command construction stays entirely in the Infrastructure
/// implementation, which reuses the shared bounded Codex process machinery every Codex role uses — never a second invocation
/// contract and never a new CLI flag, tool, permission, or session behavior. The port speaks only in project-owned terms: a
/// workspace, a sealed manifest to feed as stdin, bounded limits, and a closed outcome.
/// </summary>
public interface ICodexVerificationDiagnosisAdapter
{
    Task<VerificationDiagnosisInvocationResult> InvokeAsync(VerificationDiagnosisInvocationRequest request, CancellationToken cancellationToken);
}
