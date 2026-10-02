using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Policies.VerificationDiagnosis;
using DevalCopilot.Application.Features.Runs.Ports;

namespace DevalCopilot.Infrastructure.Features.Runs;

/// <summary>
/// Invokes the Codex CLI in bounded, read-only, non-interactive verification-diagnosis mode (ADR-0018) through the same shared
/// <see cref="CodexProcessInvoker"/> contract every Codex role uses, constrained to <see cref="VerificationDiagnosisOutputSchema"/>
/// instead — never a copied or divergent invocation contract, and never a new CLI flag, tool, permission, authentication, or
/// session behavior. This role never mutates the worktree and never runs verification itself.
/// </summary>
public sealed class CodexVerificationDiagnosisAdapter(IProcessExecutionAdapter processExecutionAdapter, IArtifactStore artifactStore)
    : ICodexVerificationDiagnosisAdapter
{
    public async Task<VerificationDiagnosisInvocationResult> InvokeAsync(
        VerificationDiagnosisInvocationRequest request, CancellationToken cancellationToken)
    {
        var outcome = await CodexProcessInvoker.InvokeAsync(
            processExecutionAdapter,
            artifactStore,
            new CodexProcessInvoker.Request(
                request.RunId,
                request.AttemptId,
                request.WorkspacePath,
                request.ContextManifestRelativeStoragePath,
                request.ContextManifestByteLength,
                request.ContextManifestContentHash,
                request.LaunchExecutablePath,
                request.LaunchScriptPath,
                request.Timeout,
                request.MaxBytesPerStream,
                request.MaxTotalCapturedBytes,
                VerificationDiagnosisOutputSchema.BuildSchemaDocument(),
                request.RequestedModel,
                request.RequestedEffort),
            cancellationToken).ConfigureAwait(false);

        return new VerificationDiagnosisInvocationResult(
            outcome.Succeeded ? VerificationDiagnosisInvocationOutcome.Exited : VerificationDiagnosisInvocationOutcome.Failed,
            outcome.StandardOutputTruncated,
            outcome.StandardErrorTruncated,
            outcome.ProviderSessionId,
            outcome.ProcessEvidence,
            outcome.TokenUsage);
    }
}
