using System.Text;
using System.Text.Json;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Policies;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Infrastructure.Features.Runs;

/// <summary>Invokes Claude with the same bounded, no-shell, isolated mutation contract as the
/// established implementation adapter, but uses the ReviewCorrection schema. ADR-0072 (contract
/// <c>claude-review-correction-v3</c>): only the current contract is dispatched, the shared
/// <see cref="ClaudeResultEnvelope"/> returns the schema-bound <c>structured_output</c> object only for
/// semantic success, and every failure carries one closed <see cref="AgentInvocationDiagnostic"/>.</summary>
public sealed class ClaudeReviewCorrectionAdapter(IProcessExecutionAdapter processExecutionAdapter, IArtifactStore artifactStore)
    : IClaudeReviewCorrectionAdapter
{
    private const int MaxManifestBytes = 32 * 1024;

    private readonly ClaudeProcessEnvironmentInputs _environmentInputs = ClaudeProcessEnvironmentInputs.Current;

    /// <summary>Test seam: the same adapter with controlled environment inputs, so its real control
    /// flow is exercised without reading a real process variable or the real system directory.</summary>
    internal ClaudeReviewCorrectionAdapter(
        IProcessExecutionAdapter processExecutionAdapter,
        IArtifactStore artifactStore,
        ClaudeProcessEnvironmentInputs environmentInputs)
        : this(processExecutionAdapter, artifactStore)
    {
        _environmentInputs = environmentInputs;
    }

    public async Task<ReviewCorrectionInvocationResult> InvokeAsync(
        ReviewCorrectionInvocationRequest request, CancellationToken cancellationToken)
    {
        if (!ClaudeMutationAdapterContract.IsCurrentVersionFor(AgentResponseContract.ReviewCorrection, request.AdapterContractVersion))
        {
            // ADR-0072: only the current contract is dispatched; a historical attempt is never upgraded or redispatched.
            return Failed(AgentInvocationDiagnostic.InvocationUnavailable);
        }

        if (!Path.IsPathFullyQualified(request.LaunchExecutablePath)
            || !File.Exists(request.LaunchExecutablePath)
            || AgentInvocationScratchDirectory.PathOrAnyAncestorHasReparsePoint(request.LaunchExecutablePath))
        {
            return Failed(AgentInvocationDiagnostic.InvocationUnavailable);
        }

        // Mirrors ClaudeCriticalReviewAdapter: a cancelled request is never masked as an environment refusal.
        cancellationToken.ThrowIfCancellationRequested();

        // Mirrors ClaudeCriticalReviewAdapter: refused before the manifest is read, any sink is
        // requested, any artifact is written or a process starts — never retried with the smaller map.
        if (!ClaudeProcessEnvironment.TryBuild(_environmentInputs, out var environment))
        {
            return Failed(AgentInvocationDiagnostic.EnvironmentUnavailable);
        }

        var manifest = await artifactStore.VerifyAndReadSealedAsync(
            request.ContextManifestRelativeStoragePath,
            request.ContextManifestByteLength,
            request.ContextManifestContentHash,
            0,
            MaxManifestBytes,
            cancellationToken);
        // The sealed manifest must agree exactly with the attempt's immutable direct-guidance snapshot carried by the
        // request; any disagreement fails closed here, before any process starts, and never reconstructs context.
        if (manifest.Status != SealedReadStatus.Ok
            || !DirectHumanGuidanceManifest.Agrees(manifest.Text, request.DirectHumanGuidance))
        {
            return Failed(AgentInvocationDiagnostic.InvocationUnavailable);
        }

        var arguments = new List<string>
        {
            "--print", "--input-format", "text", "--output-format", "json",
            "--json-schema", JsonSerializer.Serialize(ReviewCorrectionOutputSchema.BuildSchemaDocument()),
            "--safe-mode", "--restricted", "--disable-slash-commands", "--no-chrome",
            "--permission-prompts", "none",
            "--tools", "Read,Edit,Write,Glob,Grep", "--strict-mcp-config",
            "--permission-mode", "acceptEdits", "--no-session-persistence",
            "--session-id", Guid.NewGuid().ToString(),
        };

        if (!ClaudeModelRequestArguments.TryAppend(
                arguments, request.RequestedClaudeModel, request.RequestedClaudeEffort))
        {
            return Failed(AgentInvocationDiagnostic.InvocationUnavailable);
        }

        if (!ClaudeMutationTurnLimitArguments.TryAppend(
                arguments, request.RequestedMaxTurns, request.AdapterContractVersion,
                ClaudeMutationAdapterContract.CurrentReviewCorrection))
        {
            return Failed(AgentInvocationDiagnostic.InvocationUnavailable);
        }

        ProcessExecutionResult result;
        try
        {
            result = await processExecutionAdapter.ExecuteAsync(new ProcessExecutionRequest
            {
                ExecutablePath = request.LaunchExecutablePath,
                Arguments = arguments,
                WorkingDirectory = request.WorkspacePath,
                ApprovedRoot = request.WorkspacePath,
                EnvironmentVariables = environment,
                Timeout = request.Timeout,
                MaxBytesPerStream = request.MaxBytesPerStream,
                MaxTotalCapturedBytes = request.MaxTotalCapturedBytes,
                StandardOutputSinkPath = artifactStore.GetPartialPath(request.RunId, request.AttemptId, Domain.Features.Runs.ArtifactPurpose.AgentStandardOutput),
                StandardErrorSinkPath = artifactStore.GetPartialPath(request.RunId, request.AttemptId, Domain.Features.Runs.ArtifactPurpose.AgentStandardError),
                StandardInput = Encoding.UTF8.GetBytes(manifest.Text),
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Failed(AgentInvocationDiagnostic.InvocationUnavailable);
        }

        // Preserved for every real result, never collapsed into the closed Exited/Failed
        // classification alone — mirrors ClaudeImplementationAdapter.
        var processEvidence = AgentProcessEvidence.FromProcessExecutionResult(result);
        var reading = ClaudeResultEnvelope.Classify(result);
        if (!reading.IsSuccess)
        {
            return Failed(reading.Diagnostic!.Value, result, processEvidence, reading.TokenUsage, reading.ModelContextLimits);
        }

        try
        {
            var path = artifactStore.GetPartialPath(request.RunId, request.AttemptId, Domain.Features.Runs.ArtifactPurpose.AgentFinalResponse);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, reading.StructuredOutputJson, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Failed(
                AgentInvocationDiagnostic.CaptureFault, result, processEvidence, reading.TokenUsage, reading.ModelContextLimits);
        }

        return new ReviewCorrectionInvocationResult(
            ImplementationInvocationOutcome.Exited, result.StandardOutputTruncated, result.StandardErrorTruncated, reading.SessionId,
            processEvidence, reading.TokenUsage, reading.ModelContextLimits);
    }

    /// <summary>Mirrors <c>ClaudeCriticalReviewAdapter.Failed</c>.</summary>
    private static ReviewCorrectionInvocationResult Failed(
        AgentInvocationDiagnostic diagnostic,
        ProcessExecutionResult? result = null,
        AgentProcessEvidence? processEvidence = null,
        AgentTokenUsage? tokenUsage = null,
        AgentModelContextLimits? modelLimits = null) =>
        new(ImplementationInvocationOutcome.Failed, result?.StandardOutputTruncated ?? false, result?.StandardErrorTruncated ?? false,
            null, processEvidence, tokenUsage, modelLimits, diagnostic);
}
