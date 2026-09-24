using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Commands.MarkAgentAttemptDispatched;
using DevalCopilot.Application.Features.Runs.Commands.RecordAgentAttemptCheckpointEvidenceUnavailable;
using DevalCopilot.Application.Features.Runs.Commands.RecordAgentAttemptResult;
using DevalCopilot.Application.Features.Runs.Commands.RecordAgentAttemptSourceChanged;
using DevalCopilot.Application.Features.Runs.Commands.RecordAgentAttemptWorkspaceIneligible;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Application.Features.Runs.Queries.GetCodexLaunchTarget;
using DevalCopilot.Application.Features.Runs.Queries.GetEligibleAgentAttempts;
using DevalCopilot.Application.Features.Runs.Queries.GetIneligibleAgentAttempts;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Api.HostedServices;

/// <summary>
/// Executes only durably claimed Codex planning attempts, entirely outside any EF Core
/// transaction. A dispatch marker is committed before the provider is ever invoked so a later
/// polling cycle cannot invoke it twice. Never retries automatically; a host restart leaves any
/// unrecorded attempt for startup reconciliation.
/// </summary>
public sealed class AgentAttemptSupervisor(
    IServiceScopeFactory scopeFactory,
    ICodexPlanningAdapter codexAdapter,
    IGitWorkspaceEvidenceReader evidenceReader,
    IArtifactStore artifactStore,
    ILogger<AgentAttemptSupervisor> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>Deliberately independent of <c>stoppingToken</c> in both directions — host
    /// shutdown cannot cancel it early (that would lose a known result) and it cannot block
    /// shutdown indefinitely if the write ever stalls. Follows the same pattern as
    /// <c>ProcessAttemptSupervisor</c>'s own <c>RecordingTimeout</c>, but is intentionally longer
    /// (10s vs. 5s): recording an Agent result also independently validates the parsed Proposal's
    /// summary and structured content against <c>CollaborationMessageContentPolicy</c> before it
    /// can commit, real work <c>RecordProcessAttemptResultCommand</c> never performs.</summary>
    private static readonly TimeSpan RecordingTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Hard ceiling on the bytes read from the sealed final-response artifact before
    /// attempting to parse it. An artifact larger than this is treated as invalid without ever
    /// being read — the bound comes from the read itself, never a separate length pre-check.</summary>
    private const int MaxFinalResponseReadBytes = 32 * 1024;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        do
        {
            try
            {
                var attempts = await DispatchAsync(new GetEligibleAgentAttemptsQuery(), stoppingToken);
                foreach (var attempt in attempts)
                {
                    await ExecuteOneAsync(attempt, stoppingToken);
                }

                // Never left to strand: an attempt whose Ready workspace, active lease, or
                // current checkpoint stopped holding after claim is excluded from eligibility
                // above forever, so it is explicitly resolved here instead — zero provider
                // invocations for it, ever.
                var ineligible = await DispatchAsync(new GetIneligibleAgentAttemptsQuery(), stoppingToken);
                foreach (var candidate in ineligible)
                {
                    await DispatchAsync(
                        new RecordAgentAttemptWorkspaceIneligibleCommand(candidate.RunId, candidate.AttemptId), CancellationToken.None);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError("agent_attempt_supervisor_iteration_failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ExecuteOneAsync(EligibleAgentAttempt attempt, CancellationToken stoppingToken)
    {
        var preDispatchEvidence = await CaptureEvidenceSafelyAsync(attempt.WorkspacePath, stoppingToken);
        if (preDispatchEvidence.Outcome == GitWorkspaceEvidenceOutcome.Success
            && preDispatchEvidence.FingerprintSha256 is not null
            && !string.Equals(preDispatchEvidence.FingerprintSha256, attempt.CheckpointFingerprintSha256, StringComparison.Ordinal))
        {
            await DispatchAsync(
                new RecordAgentAttemptSourceChangedCommand(attempt.RunId, attempt.AttemptId), CancellationToken.None);
            return;
        }

        if (preDispatchEvidence.Outcome != GitWorkspaceEvidenceOutcome.Success || preDispatchEvidence.FingerprintSha256 is null)
        {
            // Never left Running and undispatched on a returned or thrown evidence failure — that
            // would make this attempt eligible again on the very next 500ms poll and silently
            // retry the same failing capture forever, contradicting "never retries automatically."
            // Resolved to a truthful terminal outcome instead; the provider is never invoked.
            logger.LogError("agent_attempt_pre_dispatch_evidence_unavailable AttemptId={AttemptId}", attempt.AttemptId);
            await DispatchAsync(
                new RecordAgentAttemptCheckpointEvidenceUnavailableCommand(attempt.RunId, attempt.AttemptId), CancellationToken.None);
            return;
        }

        // Resolved only from the durable, currently successful Codex capability snapshot —
        // never a fresh search through PATH or a private desktop application layout.
        var launchTarget = await DispatchAsync(new GetCodexLaunchTargetQuery(), stoppingToken);

        var dispatched = await DispatchAsync(
            new MarkAgentAttemptDispatchedCommand(attempt.RunId, attempt.AttemptId), stoppingToken);
        if (dispatched.IsFailure)
        {
            // The command's own last-gate revalidation is authoritative: eligibility could have
            // been lost between the earlier snapshot (and the evidence capture just above) and
            // this very call. That specific reason is resolved explicitly here — zero provider
            // invocations and never left to strand — every other failure reason (not found,
            // already dispatched, no longer Running) means some other path already resolved this
            // attempt, so there is nothing further to do.
            if (dispatched.Errors[0].Code == MarkAgentAttemptDispatchedCommandHandler.WorkspaceNoLongerEligibleCode)
            {
                await DispatchAsync(
                    new RecordAgentAttemptWorkspaceIneligibleCommand(attempt.RunId, attempt.AttemptId), CancellationToken.None);
            }

            return;
        }

        if (launchTarget is null)
        {
            // Durably committed to dispatch, but there is nothing to invoke: still recorded as a
            // terminal, safe, closed outcome — never left hanging, never silently skipped.
            await RecordResultAsync(attempt, AgentOutcome.ProviderInvocationFailed, standardOutputTruncated: false,
                standardErrorTruncated: false, providerSessionId: null, processEvidence: null, CancellationToken.None);
            return;
        }

        CodexPlanningInvocationResult invocationResult;
        try
        {
            invocationResult = await codexAdapter.InvokeAsync(
                new CodexPlanningInvocationRequest(
                    attempt.RunId,
                    attempt.AttemptId,
                    attempt.WorkspacePath,
                    attempt.ContextManifestRelativeStoragePath,
                    attempt.ContextManifestByteLength,
                    attempt.ContextManifestContentHash,
                    launchTarget.ExecutablePath,
                    launchTarget.ScriptPath,
                    attempt.Timeout,
                    attempt.MaxBytesPerStream,
                    attempt.MaxTotalCapturedBytes),
                stoppingToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError("agent_attempt_invocation_failed AttemptId={AttemptId}", attempt.AttemptId);
            await RecordResultAsync(attempt, AgentOutcome.ProviderInvocationFailed, standardOutputTruncated: false,
                standardErrorTruncated: false, providerSessionId: null, processEvidence: null, CancellationToken.None);
            return;
        }

        // An Exited classification is trusted only when the host-measured evidence independently
        // confirms a clean exit; contradictory or missing evidence never becomes a success.
        var outcome = invocationResult.Outcome == CodexPlanningInvocationOutcome.Exited
            && invocationResult.ProcessEvidence is { IsCleanExit: true }
                ? AgentOutcome.Proposed
                : AgentOutcome.ProviderInvocationFailed;

        await RecordResultAsync(
            attempt,
            outcome,
            invocationResult.StandardOutputTruncated,
            invocationResult.StandardErrorTruncated,
            invocationResult.ProviderSessionId,
            invocationResult.ProcessEvidence,
            CancellationToken.None);
    }

    private async Task RecordResultAsync(
        EligibleAgentAttempt attempt,
        AgentOutcome outcome,
        bool standardOutputTruncated,
        bool standardErrorTruncated,
        string? providerSessionId,
        AgentProcessEvidence? processEvidence,
        CancellationToken cancellationToken)
    {
        // Freshly recaptured after the (possibly read-only) invocation, regardless of its
        // process-level outcome: a changed fingerprint always wins inside CompleteAgent's own
        // override, so a Proposal is never recorded as evidence about stale source. Safely
        // captured: a thrown exception here must never abort recording altogether and leave this
        // already-dispatched attempt stuck Running with no result until a restart — it is treated
        // exactly like a returned capture failure below.
        var completionEvidence = await CaptureEvidenceSafelyAsync(attempt.WorkspacePath, CancellationToken.None);
        var completionFingerprint = completionEvidence.Outcome == GitWorkspaceEvidenceOutcome.Success
            ? completionEvidence.FingerprintSha256
            : null;

        if (completionFingerprint is null)
        {
            logger.LogError("agent_attempt_completion_evidence_failed AttemptId={AttemptId}", attempt.AttemptId);
        }

        var sealedArtifacts = await SealArtifactsAsync(attempt, standardOutputTruncated, standardErrorTruncated);

        // A null fingerprint means fresh evidence could not be captured at all — CompleteAgent's
        // own mismatch override never fires for a null fingerprint, so without this check a
        // successful provider exit would otherwise still be recorded Proposed despite there being
        // no confirmation the checkpoint is still current. Never parse (let alone trust) the
        // final response in that case; there is nothing evidence-bound to check it against.
        ValidatedProposal? proposal = null;
        AgentOutcome effectiveOutcome;
        if (outcome == AgentOutcome.Proposed && completionFingerprint is null)
        {
            effectiveOutcome = AgentOutcome.CheckpointEvidenceUnavailable;
        }
        else if (outcome == AgentOutcome.Proposed)
        {
            proposal = await TryParseFinalResponseAsync(attempt, sealedArtifacts);
            effectiveOutcome = proposal is null ? AgentOutcome.InvalidStructuredOutput : AgentOutcome.Proposed;
        }
        else
        {
            effectiveOutcome = outcome;
        }

        // A terminal classification exists at this point, so this short recording transaction is
        // attempted with its own bounded token: neither stoppingToken (host shutdown must not
        // cancel it early and lose a known outcome) nor CancellationToken.None (a stalled local
        // write must not block shutdown forever). If it still times out or otherwise fails, no
        // terminal result is invented, Codex is never invoked again for this attempt here, and
        // nothing retries in this pass — the attempt simply stays Running and Dispatched, exactly
        // like any other unrecorded attempt, for the next startup's reconciliation.
        using var recordingTimeoutSource = new CancellationTokenSource(RecordingTimeout);
        Result<RecordAgentAttemptResultCommandResult> recordResult;
        try
        {
            recordResult = await DispatchAsync(
                new RecordAgentAttemptResultCommand(
                    attempt.RunId, attempt.AttemptId, effectiveOutcome, completionFingerprint, sealedArtifacts, proposal, providerSessionId,
                    processEvidence),
                recordingTimeoutSource.Token);
        }
        catch (Exception)
        {
            // Covers the bounded timeout elapsing as well as any other dispatch failure. Never
            // the exception object, its message, or any command/environment/output/repository
            // detail — only a stable event code and the attempt identifier.
            logger.LogError("agent_attempt_result_recording_failed AttemptId={AttemptId}", attempt.AttemptId);
            return;
        }

        if (recordResult.IsFailure)
        {
            logger.LogError(
                "agent_attempt_result_recording_rejected AttemptId={AttemptId} ErrorCode={ErrorCode}",
                attempt.AttemptId, recordResult.Errors[0].Code);
            return;
        }

        // Notify only after durable recording — never before.
        await using var scope = scopeFactory.CreateAsyncScope();
        var notifier = scope.ServiceProvider.GetRequiredService<IRunEventNotifier>();
        await notifier.NotifyRunAdvancedAsync(attempt.RunId, recordResult.Value.LatestEventSequence, cancellationToken);
    }

    private async Task<IReadOnlyList<SealedAgentArtifact>> SealArtifactsAsync(
        EligibleAgentAttempt attempt, bool standardOutputTruncated, bool standardErrorTruncated)
    {
        var results = new List<SealedAgentArtifact>();
        foreach (var (purpose, truncated) in new[]
                 {
                     (ArtifactPurpose.AgentStandardOutput, standardOutputTruncated),
                     (ArtifactPurpose.AgentStandardError, standardErrorTruncated),
                     (ArtifactPurpose.AgentFinalResponse, false),
                 })
        {
            var sealedFile = await artifactStore.SealAsync(attempt.RunId, attempt.AttemptId, purpose, CancellationToken.None);
            if (sealedFile is not null)
            {
                results.Add(new SealedAgentArtifact(purpose, sealedFile.RelativeStoragePath, sealedFile.ByteLength, sealedFile.ContentHash, truncated));
            }
        }

        return results;
    }

    private async Task<ValidatedProposal?> TryParseFinalResponseAsync(
        EligibleAgentAttempt attempt, IReadOnlyList<SealedAgentArtifact> sealedArtifacts)
    {
        var finalResponse = sealedArtifacts.SingleOrDefault(artifact => artifact.Purpose == ArtifactPurpose.AgentFinalResponse);
        if (finalResponse is null || finalResponse.ByteLength > MaxFinalResponseReadBytes)
        {
            return null;
        }

        var window = await artifactStore.VerifyAndReadSealedAsync(
            finalResponse.RelativeStoragePath,
            finalResponse.ByteLength,
            finalResponse.ContentHash,
            fromOffset: 0,
            maxBytes: MaxFinalResponseReadBytes,
            CancellationToken.None);

        return window.Status != SealedReadStatus.Ok ? null : CodexFinalResponseParser.TryParse(window.Text);
    }

    /// <summary>
    /// Wraps <see cref="IGitWorkspaceEvidenceReader.CaptureAsync"/> so a thrown, non-cancellation
    /// exception is classified exactly like a returned capture failure
    /// (<see cref="GitWorkspaceEvidenceOutcome.GitInvocationFailed"/>) rather than propagating out
    /// and aborting whichever caller was mid-flow — cancellation from actual host shutdown is
    /// still rethrown unchanged.
    /// </summary>
    private async Task<GitWorkspaceEvidenceResult> CaptureEvidenceSafelyAsync(string workspacePath, CancellationToken cancellationToken)
    {
        try
        {
            return await evidenceReader.CaptureAsync(workspacePath, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            logger.LogError("agent_attempt_evidence_capture_threw");
            return new GitWorkspaceEvidenceResult(GitWorkspaceEvidenceOutcome.GitInvocationFailed, null, null, [], null);
        }
    }

    private async Task<TResult> DispatchAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IApplicationMediator>().SendAsync(command, cancellationToken);
    }

    private async Task<TResult> DispatchAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IApplicationMediator>().SendAsync(query, cancellationToken);
    }
}
