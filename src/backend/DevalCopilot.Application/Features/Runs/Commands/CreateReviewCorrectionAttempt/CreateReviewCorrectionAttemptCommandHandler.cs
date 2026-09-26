using System.Text;
using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Commands.CreateReviewCorrectionAttempt;

/// <summary>Claims a correction only from a completed, applicable changes-requested review. The
/// exact ordered input messages are persisted before any external invocation can occur.</summary>
public sealed class CreateReviewCorrectionAttemptCommandHandler(
    IDevalCopilotDbContext dbContext,
    IGitWorkspaceEvidenceReader evidenceReader,
    IArtifactStore artifactStore,
    TimeProvider timeProvider,
    IRunEventNotifier? eventNotifier = null)
    : ICommandHandler<CreateReviewCorrectionAttemptCommand, Result<CreateReviewCorrectionAttemptCommandResult>>
{
    private const int MaxContextManifestBytes = 32 * 1024;
    private static readonly TimeSpan InvocationTimeout = AgentClaimPathPolicy.GetInvocationTimeout(AgentClaimPath.ReviewCorrection);
    private const int MaxBytesPerStream = 256 * 1024;
    private const int MaxTotalCapturedBytes = 512 * 1024;

    public async Task<Result<CreateReviewCorrectionAttemptCommandResult>> HandleAsync(
        CreateReviewCorrectionAttemptCommand command, CancellationToken cancellationToken)
    {
        var run = await dbContext.Runs.SingleOrDefaultAsync(candidate => candidate.Id == command.RunId, cancellationToken);
        if (run is null)
        {
            return Failure(Error.NotFound("runs.not_found", "The requested run was not found."));
        }

        if (run.Lifecycle != RunLifecycle.Running)
        {
            return Failure(Error.Conflict("runs.not_running", "The run is not active."));
        }

        if (await dbContext.Attempts.AnyAsync(candidate => candidate.RunId == run.Id && candidate.Status == AttemptStatus.Running, cancellationToken))
        {
            return Failure(Error.Conflict("attempts.run_has_active_attempt", "This run already has an attempt in progress."));
        }

        // The run-wide Agent claim budget: every claimed Agent attempt, regardless of role,
        // provider, dispatch, result, or interruption, permanently consumes one slot. Checked
        // first, before any review-correction-specific budget/escalation/authorization logic
        // below, so a globally exhausted run never consumes a review-correction human
        // authorization and never invokes a provider. The filtered unique index on
        // (RunId, AgentBudgetSlot) is the database backstop for the race this count-and-compare
        // check alone cannot close.
        var agentAttemptsUsed = await dbContext.Attempts.CountAsync(
            candidate => candidate.RunId == run.Id && candidate.Kind == AttemptKind.Agent,
            cancellationToken);
        if (agentAttemptsUsed >= run.MaximumAgentAttempts)
        {
            return Failure(Error.Conflict("agent_attempts.budget_exhausted", "This run has reached its maximum claimed Agent attempts."));
        }

        // The independent run-wide Agent invocation-TIME budget (see the companion ADR to
        // ADR-0012), enforced alongside — never instead of — the count-based budget above, still
        // first, before the review-correction-specific budget/escalation/authorization logic
        // below, so a globally time-exhausted run never consumes a review-correction human
        // authorization and never invokes a provider. A run with no time-budget policy (a
        // historical Run predating this decision) skips this check entirely rather than being
        // bound by a fabricated ceiling.
        if (run.MaximumAgentInvocationTime is { } maximumAgentInvocationTime)
        {
            var reservedAgentInvocationTime = await AgentInvocationTimeBudget.ComputeReservedAsync(dbContext, run.Id, asNoTracking: false, cancellationToken);
            if (reservedAgentInvocationTime is null)
            {
                return Failure(Error.Failure("agent_attempts.time_budget_evidence_invalid", "This claim's reserved Agent invocation time could not be safely evaluated: prior Agent attempt invocation-time evidence is missing or invalid, or this claim's own reservation is not representable."));
            }

            var projectedAgentInvocationTime = AgentInvocationTimeReservation.ComputeProjectedReservation(reservedAgentInvocationTime.Value, InvocationTimeout);
            if (projectedAgentInvocationTime is null)
            {
                return Failure(Error.Failure("agent_attempts.time_budget_evidence_invalid", "This claim's reserved Agent invocation time could not be safely evaluated: prior Agent attempt invocation-time evidence is missing or invalid, or this claim's own reservation is not representable."));
            }

            if (projectedAgentInvocationTime.Value > maximumAgentInvocationTime)
            {
                return Failure(Error.Conflict("agent_attempts.time_budget_exceeded", "This run has reached its maximum reserved Agent invocation time."));
            }
        }

        var workspace = await dbContext.GitWorkspaces
            .Where(candidate => candidate.ProjectId == run.ProjectId)
            .OrderByDescending(candidate => candidate.WorkspaceNumber)
            .FirstOrDefaultAsync(cancellationToken);
        if (workspace is null || workspace.Status != WorkspaceStatus.Ready)
        {
            return Failure(Error.Conflict("agent_attempts.workspace_not_ready", "A ready isolated workspace is required."));
        }

        if (!await dbContext.RepositoryMutationLeases.AnyAsync(
                lease => lease.WorkspaceId == workspace.Id && lease.Status == LeaseStatus.Active, cancellationToken))
        {
            return Failure(Error.Conflict("agent_attempts.lease_not_active", "An active workspace lease is required."));
        }

        var checkpoint = await dbContext.GitCheckpoints
            .Where(candidate => candidate.WorkspaceId == workspace.Id)
            .OrderByDescending(candidate => candidate.CheckpointNumber)
            .FirstOrDefaultAsync(cancellationToken);
        if (checkpoint is null)
        {
            return Failure(Error.Conflict("agent_attempts.checkpoint_missing", "A current Git checkpoint is required."));
        }

        var evidence = await evidenceReader.CaptureAsync(workspace.WorkspacePath, cancellationToken);
        if (evidence.Outcome != GitWorkspaceEvidenceOutcome.Success
            || !string.Equals(evidence.FingerprintSha256, checkpoint.FingerprintSha256, StringComparison.Ordinal))
        {
            return Failure(Error.Conflict("agent_attempts.checkpoint_not_current", "The selected source checkpoint is no longer current."));
        }

        var review = await dbContext.Attempts.SingleOrDefaultAsync(
            candidate => candidate.Id == command.ImplementationReviewAttemptId && candidate.RunId == run.Id,
            cancellationToken);
        if (review is null
            || review.Kind != AttemptKind.Agent
            || review.AgentRole != AgentRole.CodeReviewer
            || review.AgentResponseContract != AgentResponseContract.ImplementationReview
            || review.Status != AttemptStatus.Completed
            || review.AgentOutcome != AgentOutcome.ReviewChangesRequested
            || review.AgentGitWorkspaceId != workspace.Id
            || review.AgentGitCheckpointId != checkpoint.Id
            || !string.Equals(review.AgentCheckpointFingerprintSha256, checkpoint.FingerprintSha256, StringComparison.Ordinal))
        {
            return Failure(Error.Conflict("agent_attempts.review_not_applicable", "The selected review is not an applicable changes-requested review."));
        }

        var persistedFindingCount = await dbContext.CollaborationMessages.CountAsync(
            message => message.AttemptId == review.Id && message.Type == CollaborationMessageType.ReviewFinding,
            cancellationToken);
        if (persistedFindingCount == 0)
        {
            return Failure(Error.Conflict("agent_attempts.review_findings_invalid", "The review has no valid bounded finding set."));
        }

        var snapshot = await ImplementerExecutionReportEligibility.LoadSnapshotAsync(dbContext, run.Id, cancellationToken);
        var currentReview = ReviewCorrectionReviewEligibility.ResolveCurrent(
            snapshot, run.Id, workspace.Id, checkpoint.Id);
        if (currentReview is null
            || currentReview.ReviewAttempt.Id != review.Id
            || currentReview.ReviewAttempt.AgentCheckpointFingerprintSha256 != checkpoint.FingerprintSha256
            || currentReview.OrderedFindings.Count > ReviewCorrectionOutputSchema.MaximumFindings)
        {
            return Failure(Error.Conflict("agent_attempts.review_not_applicable", "The selected review is not an applicable changes-requested review."));
        }

        var executionReport = currentReview.ExecutionReport;
        var findings = currentReview.OrderedFindings;

        var correctionAttemptsUsed = await dbContext.Attempts.CountAsync(
            candidate => candidate.RunId == run.Id
                && candidate.Kind == AttemptKind.Agent
                && candidate.AgentResponseContract == AgentResponseContract.ReviewCorrection,
            cancellationToken);
        ReviewCorrectionAuthorization? authorization = null;
        if (correctionAttemptsUsed >= run.MaximumReviewCorrectionAttempts)
        {
            var escalation = await dbContext.ReviewCorrectionEscalations
                .SingleOrDefaultAsync(candidate => candidate.RunId == run.Id && candidate.ImplementationReviewAttemptId == review.Id, cancellationToken);
            if (escalation is not null)
            {
                authorization = await dbContext.ReviewCorrectionAuthorizations
                    .Where(candidate => candidate.EscalationId == escalation.Id && candidate.ConsumedByAttemptId == null)
                    .SingleOrDefaultAsync(cancellationToken);
            }

            if (authorization is null)
            {
                return await CreateOrGetEscalationAsync(run, review, executionReport, timeProvider.GetUtcNow(), cancellationToken);
            }
        }

        var claudeSnapshot = await dbContext.HostCapabilitySnapshots
            .SingleOrDefaultAsync(candidate => candidate.Capability == Capability.ClaudeCli, cancellationToken);
        if (claudeSnapshot is null
            || claudeSnapshot.ReasonCode != CapabilityProbeReason.None
            || string.IsNullOrWhiteSpace(claudeSnapshot.ResolvedExecutablePath))
        {
            return Failure(Error.Conflict("agent_attempts.provider_not_observed", "The Claude runtime is not currently observed as available."));
        }

        var attemptId = Guid.NewGuid();
        if (await ReviewCorrectionInputIdentity.HasCompetingSuccessfulCorrectionAsync(
                dbContext,
                run.Id,
                attemptId,
                checkpoint.Id,
                [executionReport.Id, .. findings.Select(finding => finding.Id)],
                cancellationToken))
        {
            return Failure(Error.Conflict("agent_attempts.already_corrected", "This exact correction input already has a successful correction."));
        }

        var manifestJson = ReviewCorrectionContextManifestBuilder.Build(
            run.ProjectId,
            run.Id,
            workspace.Id,
            checkpoint.Id,
            checkpoint.FingerprintSha256,
            run.Objective,
            executionReport.Id,
            executionReport.Summary,
            executionReport.StructuredContentJson,
            findings.Select(finding => new ReviewCorrectionContextManifestBuilder.Finding(
                finding.Id, finding.Summary, finding.StructuredContentJson)).ToArray(),
            evidence.ChangedPaths,
            evidence.CompleteDiff);
        if (Encoding.UTF8.GetByteCount(manifestJson) > MaxContextManifestBytes)
        {
            return Failure(Error.Failure("agent_attempts.context_manifest_too_large", "The context manifest exceeds its bound."));
        }

        var manifestArtifactId = Guid.NewGuid();
        var manifestPartialPath = artifactStore.GetPartialPath(run.Id, attemptId, ArtifactPurpose.AgentContextManifest);
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPartialPath)!);
        await File.WriteAllTextAsync(manifestPartialPath, manifestJson, cancellationToken);
        var sealedManifest = await artifactStore.SealAsync(run.Id, attemptId, ArtifactPurpose.AgentContextManifest, cancellationToken);
        if (sealedManifest is null)
        {
            return Failure(Error.Failure("agent_attempts.context_manifest_seal_failed", "The context manifest could not be sealed."));
        }

        var attemptNumber = await dbContext.Attempts.Where(candidate => candidate.RunId == run.Id).CountAsync(cancellationToken) + 1;
        var agentBudgetSlot = agentAttemptsUsed + 1;
        var nowUtc = timeProvider.GetUtcNow();
        var attempt = Attempt.ClaimAgentReviewCorrection(
            attemptId,
            run.Id,
            attemptNumber,
            workspace.Id,
            checkpoint.Id,
            checkpoint.FingerprintSha256,
            manifestArtifactId,
            InvocationTimeout,
            MaxBytesPerStream,
            MaxTotalCapturedBytes,
            nowUtc,
            agentBudgetSlot);
        authorization?.Consume(attemptId, nowUtc);
        dbContext.Attempts.Add(attempt);

        var inputRows = new List<AttemptInputMessage>(findings.Count + 1)
        {
            AttemptInputMessage.Record(Guid.NewGuid(), attemptId, executionReport.Id, 0),
        };
        inputRows.AddRange(findings.Select((finding, index) => AttemptInputMessage.Record(
            Guid.NewGuid(), attemptId, finding.Id, index + 1)));
        dbContext.AttemptInputMessages.AddRange(inputRows);

        var manifestArtifact = Artifact.Record(
            manifestArtifactId,
            run.Id,
            attemptId,
            ArtifactPurpose.AgentContextManifest,
            "application/json",
            sealedManifest.RelativeStoragePath,
            sealedManifest.ContentHash,
            sealedManifest.ByteLength,
            false,
            ArtifactCaptureOutcome.Captured,
            ArtifactSensitivity.HostConstructedContent,
            ArtifactRetentionPolicy.RetainUntilRunDeleted,
            nowUtc);
        dbContext.Artifacts.Add(manifestArtifact);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            var thisAttemptPersisted = await dbContext.Attempts
                .AsNoTracking()
                .AnyAsync(candidate => candidate.Id == attemptId, cancellationToken);
            if (thisAttemptPersisted)
            {
                return Result<CreateReviewCorrectionAttemptCommandResult>.Success(
                    new CreateReviewCorrectionAttemptCommandResult.AttemptCreated(attemptId, attemptNumber));
            }

            artifactStore.DeleteOrphanedSealedFile(run.Id, attemptId, ArtifactPurpose.AgentContextManifest);
            dbContext.Attempts.Remove(attempt);
            foreach (var inputMessage in inputRows)
            {
                dbContext.AttemptInputMessages.Remove(inputMessage);
            }

            dbContext.Artifacts.Remove(manifestArtifact);

            var competingRunningAttemptExists = await dbContext.Attempts
                .AsNoTracking()
                .AnyAsync(candidate => candidate.RunId == run.Id && candidate.Status == AttemptStatus.Running, cancellationToken);
            if (competingRunningAttemptExists)
            {
                return Failure(Error.Conflict("attempts.run_has_active_attempt", "This run already has an attempt in progress."));
            }

            // The race the (RunId, AgentBudgetSlot) unique index exists to close: a concurrent
            // request already consumed the exact slot this request also computed. Below the
            // maximum, this is a safe, retryable conflict — only this one slot number was lost to
            // a faster concurrent claim, not the run's whole budget. Only when the run's real
            // Agent-attempt count has already reached its maximum is this truthfully exhaustion.
            var slotAlreadyClaimedByAnotherAttempt = await dbContext.Attempts.AsNoTracking().AnyAsync(
                candidate => candidate.RunId == run.Id && candidate.Kind == AttemptKind.Agent && candidate.AgentBudgetSlot == agentBudgetSlot,
                cancellationToken);
            if (slotAlreadyClaimedByAnotherAttempt)
            {
                var agentAttemptsUsedNow = await dbContext.Attempts.AsNoTracking().CountAsync(
                    candidate => candidate.RunId == run.Id && candidate.Kind == AttemptKind.Agent, cancellationToken);
                if (agentAttemptsUsedNow >= run.MaximumAgentAttempts)
                {
                    return Failure(Error.Conflict("agent_attempts.budget_exhausted", "This run has reached its maximum claimed Agent attempts."));
                }

                // Distinguishes a genuine time-budget rejection from a merely lost slot race: a
                // concurrent claim that also committed real reserved time can push this run over
                // its time ceiling even while count capacity remains, and that must never be
                // misreported as a retryable slot conflict.
                if (run.MaximumAgentInvocationTime is { } maximumAgentInvocationTimeOnRace)
                {
                    var reservedAgentInvocationTimeNow = await AgentInvocationTimeBudget.ComputeReservedAsync(dbContext, run.Id, asNoTracking: true, cancellationToken);
                    if (reservedAgentInvocationTimeNow is null)
                    {
                        return Failure(Error.Failure("agent_attempts.time_budget_evidence_invalid", "This claim's reserved Agent invocation time could not be safely evaluated: prior Agent attempt invocation-time evidence is missing or invalid, or this claim's own reservation is not representable."));
                    }

                    var projectedAgentInvocationTimeOnRace = AgentInvocationTimeReservation.ComputeProjectedReservation(reservedAgentInvocationTimeNow.Value, InvocationTimeout);
                    if (projectedAgentInvocationTimeOnRace is null)
                    {
                        return Failure(Error.Failure("agent_attempts.time_budget_evidence_invalid", "This claim's reserved Agent invocation time could not be safely evaluated: prior Agent attempt invocation-time evidence is missing or invalid, or this claim's own reservation is not representable."));
                    }

                    if (projectedAgentInvocationTimeOnRace.Value > maximumAgentInvocationTimeOnRace)
                    {
                        return Failure(Error.Conflict("agent_attempts.time_budget_exceeded", "This run has reached its maximum reserved Agent invocation time."));
                    }
                }

                return Failure(Error.Conflict("agent_attempts.budget_slot_conflict", "A concurrent request already claimed this Agent attempt's budget slot; retry the request."));
            }

            return Failure(Error.Failure("attempts.persistence_failed", "The correction attempt could not be durably recorded."));
        }

        return Result<CreateReviewCorrectionAttemptCommandResult>.Success(
            new CreateReviewCorrectionAttemptCommandResult.AttemptCreated(attempt.Id, attempt.AttemptNumber));
    }

    private async Task<Result<CreateReviewCorrectionAttemptCommandResult>> CreateOrGetEscalationAsync(
        Run run,
        Attempt review,
        CollaborationMessage executionReport,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.ReviewCorrectionEscalations
            .SingleOrDefaultAsync(candidate => candidate.RunId == run.Id && candidate.ImplementationReviewAttemptId == review.Id, cancellationToken);
        if (existing is not null)
        {
            var existingEventSequence = await FindCollaborationMessageEventSequenceAsync(
                run.Id, existing.CollaborationMessageId, cancellationToken);
            if (existingEventSequence is not { } sequence)
            {
                return Failure(Error.Failure(
                    "review_correction_escalations.event_missing",
                    "The persisted escalation event could not be recovered."));
            }

            if (eventNotifier is not null)
            {
                await eventNotifier.NotifyRunAdvancedAsync(run.Id, sequence, cancellationToken);
            }

            return Result<CreateReviewCorrectionAttemptCommandResult>.Success(
                new CreateReviewCorrectionAttemptCommandResult.Escalated(existing.Id, existing.CollaborationMessageId, sequence));
        }

        var message = CollaborationMessage.Record(
            Guid.NewGuid(),
            run.Id,
            null,
            CollaborationMessage.ProtocolVersionOne,
            ParticipantIdentity.ForOrchestrator(),
            ParticipantIdentity.ForHuman(),
            CollaborationMessageType.Escalation,
            executionReport.Id,
            "Review correction requires an explicit human decision.",
            "{\"unresolvedDecision\":\"The implementation review requested changes and remains unresolved.\",\"options\":\"Authorize one additional correction attempt, or stop and review the evidence manually.\",\"consequences\":\"Continuing authorizes one bounded correction claim; stopping leaves the run awaiting human action.\",\"evidence\":\"The configured review-correction attempt limit was reached.\",\"recommendedChoice\":\"Stop unless the next correction is explicitly justified.\"}",
            CollaborationMessageProvenance.HostConstructed,
            nowUtc);
        var escalation = ReviewCorrectionEscalation.Record(Guid.NewGuid(), run.Id, review.Id, message.Id, nowUtc);
        dbContext.CollaborationMessages.Add(message);
        dbContext.ReviewCorrectionEscalations.Add(escalation);
        var escalationEvent = RunEvent.Record(
            Guid.NewGuid(), run.Id, null, RunEventType.CollaborationMessageRecorded, message.Actor,
            "{\"messageId\":\"" + message.Id + "\",\"type\":\"Escalation\",\"provenance\":\"HostConstructed\"}", nowUtc);
        dbContext.Events.Add(escalationEvent);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            var concurrent = await dbContext.ReviewCorrectionEscalations.AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.RunId == run.Id && candidate.ImplementationReviewAttemptId == review.Id, cancellationToken);
            if (concurrent is null)
            {
                throw;
            }

            var concurrentEventSequence = await FindCollaborationMessageEventSequenceAsync(
                run.Id, concurrent.CollaborationMessageId, cancellationToken);
            if (concurrentEventSequence is not { } sequence)
            {
                return Failure(Error.Failure(
                    "review_correction_escalations.event_missing",
                    "The persisted escalation event could not be recovered."));
            }

            if (eventNotifier is not null)
            {
                await eventNotifier.NotifyRunAdvancedAsync(run.Id, sequence, cancellationToken);
            }

            return Result<CreateReviewCorrectionAttemptCommandResult>.Success(
                new CreateReviewCorrectionAttemptCommandResult.Escalated(concurrent.Id, concurrent.CollaborationMessageId, sequence));
        }

        if (eventNotifier is not null)
        {
            await eventNotifier.NotifyRunAdvancedAsync(run.Id, escalationEvent.Sequence, cancellationToken);
        }

        return Result<CreateReviewCorrectionAttemptCommandResult>.Success(
            new CreateReviewCorrectionAttemptCommandResult.Escalated(escalation.Id, message.Id, escalationEvent.Sequence));
    }

    private async Task<long?> FindCollaborationMessageEventSequenceAsync(
        Guid runId,
        Guid messageId,
        CancellationToken cancellationToken) =>
        await dbContext.Events.AsNoTracking()
            .Where(candidate => candidate.RunId == runId
                && candidate.EventType == RunEventType.CollaborationMessageRecorded
                && candidate.PayloadJson.Contains(messageId.ToString()))
            .Select(candidate => (long?)candidate.Sequence)
            .SingleOrDefaultAsync(cancellationToken);

    private static Result<CreateReviewCorrectionAttemptCommandResult> Failure(Error error) =>
        Result<CreateReviewCorrectionAttemptCommandResult>.Failure(error);
}
