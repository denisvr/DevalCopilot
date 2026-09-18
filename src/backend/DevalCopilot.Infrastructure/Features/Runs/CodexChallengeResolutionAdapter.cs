using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Ports;

namespace DevalCopilot.Infrastructure.Features.Runs;

/// <summary>
/// Invokes the Codex CLI in bounded, read-only, non-interactive challenge-resolution mode using
/// the same shared <see cref="CodexProcessInvoker"/> contract <see cref="CodexPlanningAdapter"/>
/// uses, constrained to <see cref="ChallengeResolutionOutputSchema"/> instead of
/// <see cref="CodexProposalOutputSchema"/> — never a copied or divergent invocation contract.
/// </summary>
public sealed class CodexChallengeResolutionAdapter(IProcessExecutionAdapter processExecutionAdapter, IArtifactStore artifactStore)
    : ICodexChallengeResolutionAdapter
{
    public async Task<ChallengeResolutionInvocationResult> InvokeAsync(
        ChallengeResolutionInvocationRequest request, CancellationToken cancellationToken)
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
                ChallengeResolutionOutputSchema.BuildSchemaDocument()),
            cancellationToken).ConfigureAwait(false);

        return new ChallengeResolutionInvocationResult(
            outcome.Succeeded ? ChallengeResolutionInvocationOutcome.Exited : ChallengeResolutionInvocationOutcome.Failed,
            outcome.StandardOutputTruncated,
            outcome.StandardErrorTruncated,
            outcome.ProviderSessionId);
    }
}
