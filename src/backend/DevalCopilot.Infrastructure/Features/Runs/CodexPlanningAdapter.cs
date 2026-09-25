using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Ports;

namespace DevalCopilot.Infrastructure.Features.Runs;

/// <summary>
/// Invokes the Codex CLI in bounded, read-only, non-interactive planning mode using the shared
/// <see cref="CodexProcessInvoker"/> contract, constrained to <see cref="CodexProposalOutputSchema"/>.
/// </summary>
public sealed class CodexPlanningAdapter(IProcessExecutionAdapter processExecutionAdapter, IArtifactStore artifactStore)
    : ICodexPlanningAdapter
{
    public async Task<CodexPlanningInvocationResult> InvokeAsync(
        CodexPlanningInvocationRequest request, CancellationToken cancellationToken)
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
                CodexProposalOutputSchema.BuildSchemaDocument()),
            cancellationToken).ConfigureAwait(false);

        return new CodexPlanningInvocationResult(
            outcome.Succeeded ? CodexPlanningInvocationOutcome.Exited : CodexPlanningInvocationOutcome.Failed,
            outcome.StandardOutputTruncated,
            outcome.StandardErrorTruncated,
            outcome.ProviderSessionId,
            outcome.ProcessEvidence,
            outcome.TokenUsage);
    }
}
