using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Ports;

namespace DevalCopilot.Infrastructure.Features.Runs;

/// <summary>
/// Invokes the Codex CLI in bounded, read-only, non-interactive implementation-review mode using
/// the same shared <see cref="CodexProcessInvoker"/> contract <see cref="CodexPlanningAdapter"/>
/// and <see cref="CodexChallengeResolutionAdapter"/> use, constrained to
/// <see cref="ImplementationReviewOutputSchema"/> instead — never a copied or divergent invocation
/// contract, and never a new CLI flag. This role never mutates the worktree: it shares no
/// Git-mutation, process, or network capability with <see cref="Infrastructure.Features.Runs.ClaudeImplementationAdapter"/>.
/// </summary>
public sealed class CodexImplementationReviewAdapter(IProcessExecutionAdapter processExecutionAdapter, IArtifactStore artifactStore)
    : ICodexImplementationReviewAdapter
{
    public async Task<ImplementationReviewInvocationResult> InvokeAsync(
        ImplementationReviewInvocationRequest request, CancellationToken cancellationToken)
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
                ImplementationReviewOutputSchema.BuildSchemaDocument()),
            cancellationToken).ConfigureAwait(false);

        return new ImplementationReviewInvocationResult(
            outcome.Succeeded ? ImplementationReviewInvocationOutcome.Exited : ImplementationReviewInvocationOutcome.Failed,
            outcome.StandardOutputTruncated,
            outcome.StandardErrorTruncated,
            outcome.ProviderSessionId,
            outcome.ProcessEvidence);
    }
}
