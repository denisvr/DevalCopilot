using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Commands.CreateClaudeCriticalReviewAttempt;

/// <summary>
/// Claims one durable Claude critical-review attempt. Mirrors
/// <c>CreateCodexPlanningAttemptCommandHandler</c>'s workspace/lease/checkpoint eligibility chain
/// and sealed-manifest/persistence-race handling exactly, plus the additional chain of checks that
/// the exact input Proposal is real, current, and not already successfully reviewed.
/// </summary>
public sealed class CreateClaudeCriticalReviewAttemptCommandHandler(
    IDevalCopilotDbContext dbContext,
    IGitWorkspaceEvidenceReader evidenceReader,
    IArtifactStore artifactStore,
    TimeProvider timeProvider)
    : ICommandHandler<CreateClaudeCriticalReviewAttemptCommand, Result<CreateClaudeCriticalReviewAttemptCommandResult>>
{
    /// <summary>Hard ceiling on the sealed context-manifest artifact — a bounded reference
    /// document, never a transcript or repository copy.</summary>
    private const int MaxContextManifestBytes = 32 * 1024;

    private static readonly TimeSpan InvocationTimeout = TimeSpan.FromMinutes(10);
    private const int MaxBytesPerStream = 256 * 1024;
    private const int MaxTotalCapturedBytes = 512 * 1024;

    public async Task<Result<CreateClaudeCriticalReviewAttemptCommandResult>> HandleAsync(
        CreateClaudeCriticalReviewAttemptCommand command, CancellationToken cancellationToken)
    {
        var run = await dbContext.Runs.SingleOrDefaultAsync(candidate => candidate.Id == command.RunId, cancellationToken);
        if (run is null)
        {
            return Result<CreateClaudeCriticalReviewAttemptCommandResult>.Failure(
                Error.NotFound("runs.not_found", "The requested run was not found."));
        }

        if (run.Lifecycle is not (RunLifecycle.Created or RunLifecycle.Running))
        {
            return Result<CreateClaudeCriticalReviewAttemptCommandResult>.Failure(
                Error.Conflict("runs.not_active", $"The run is {run.Lifecycle} and cannot start a critical-review attempt."));
        }

        var workspace = await dbContext.GitWorkspaces
            .Where(candidate => candidate.ProjectId == run.ProjectId)
            .OrderByDescending(candidate => candidate.WorkspaceNumber)
            .FirstOrDefaultAsync(cancellationToken);
        if (workspace is null || workspace.Status != WorkspaceStatus.Ready)
        {
            return Result<CreateClaudeCriticalReviewAttemptCommandResult>.Failure(
                Error.Conflict("agent_attempts.workspace_not_ready", "A ready isolated workspace is required to request a Claude critical review."));
        }

        var leaseIsActive = await dbContext.RepositoryMutationLeases
            .AnyAsync(lease => lease.WorkspaceId == workspace.Id && lease.Status == LeaseStatus.Active, cancellationToken);
        if (!leaseIsActive)
        {
            return Result<CreateClaudeCriticalReviewAttemptCommandResult>.Failure(
                Error.Conflict("agent_attempts.lease_not_active", "An active workspace lease is required to request a Claude critical review."));
        }

        var checkpoint = await dbContext.GitCheckpoints
            .Where(candidate => candidate.WorkspaceId == workspace.Id)
            .OrderByDescending(candidate => candidate.CheckpointNumber)
            .FirstOrDefaultAsync(cancellationToken);
        if (checkpoint is null)
        {
            return Result<CreateClaudeCriticalReviewAttemptCommandResult>.Failure(
                Error.Conflict("agent_attempts.checkpoint_missing", "A current Git checkpoint is required to request a Claude critical review."));
        }

        // Run-wide, not Agent-scoped: a Simulated, Process, or Codex-planning Agent attempt
        // already Running for this run is exactly as disqualifying as another critical-review
        // attempt already Running — only one attempt of any kind may ever be Running for a run
        // at a time. The filtered unique index on (RunId WHERE Status = 'Running') is the
        // database backstop for the race this check alone cannot close.
        var alreadyRunning = await dbContext.Attempts.AnyAsync(
            candidate => candidate.RunId == run.Id && candidate.Status == AttemptStatus.Running,
            cancellationToken);
        if (alreadyRunning)
        {
            return Result<CreateClaudeCriticalReviewAttemptCommandResult>.Failure(
                Error.Conflict("attempts.run_has_active_attempt", "This run already has an attempt in progress."));
        }

        var claudeSnapshot = await dbContext.HostCapabilitySnapshots
            .SingleOrDefaultAsync(candidate => candidate.Capability == Capability.ClaudeCli, cancellationToken);
        if (claudeSnapshot is null
            || claudeSnapshot.ReasonCode != CapabilityProbeReason.None
            || claudeSnapshot.LaunchKind != CapabilityLaunchKind.DirectExecutable
            || string.IsNullOrWhiteSpace(claudeSnapshot.ResolvedExecutablePath))
        {
            return Result<CreateClaudeCriticalReviewAttemptCommandResult>.Failure(
                Error.Conflict("agent_attempts.provider_not_observed", "The Claude Code runtime is not currently observed as available."));
        }

        var evidence = await evidenceReader.CaptureAsync(workspace.WorkspacePath, cancellationToken);
        if (evidence.Outcome != GitWorkspaceEvidenceOutcome.Success || evidence.FingerprintSha256 != checkpoint.FingerprintSha256)
        {
            return Result<CreateClaudeCriticalReviewAttemptCommandResult>.Failure(
                Error.Conflict("agent_attempts.checkpoint_not_current", "The selected source checkpoint is no longer current for this workspace."));
        }

        var proposalValidation = await ValidateReviewedProposalAsync(run.Id, command.ProposalMessageId, workspace.Id, checkpoint, cancellationToken);
        if (proposalValidation.Error is { } error)
        {
            return Result<CreateClaudeCriticalReviewAttemptCommandResult>.Failure(error);
        }

        var proposalMessage = proposalValidation.Message!;

        var alreadyReviewed = await dbContext.Attempts
            .Join(
                dbContext.AttemptInputMessages.Where(inputMessage => inputMessage.CollaborationMessageId == proposalMessage.Id),
                attempt => attempt.Id,
                inputMessage => inputMessage.AttemptId,
                (attempt, inputMessage) => attempt)
            .AnyAsync(
                candidate =>
                    candidate.Kind == AttemptKind.Agent
                    && candidate.AgentRole == AgentRole.CriticalReviewer
                    && (candidate.AgentOutcome == AgentOutcome.Accepted || candidate.AgentOutcome == AgentOutcome.Challenged),
                cancellationToken);
        if (alreadyReviewed)
        {
            return Result<CreateClaudeCriticalReviewAttemptCommandResult>.Failure(
                Error.Conflict("agent_attempts.already_reviewed", "This proposal already has a successful critical review."));
        }

        var attemptId = Guid.NewGuid();
        var manifestArtifactId = Guid.NewGuid();
        var nowUtc = timeProvider.GetUtcNow();

        var manifestJson = ClaudeCriticalReviewContextManifestBuilder.Build(
            run.ProjectId,
            workspace.Id,
            checkpoint.Id,
            checkpoint.FingerprintSha256,
            run.Objective,
            proposalMessage.Id,
            proposalMessage.Summary,
            proposalMessage.StructuredContentJson,
            evidence.ChangedPaths,
            evidence.CompleteDiff);
        if (System.Text.Encoding.UTF8.GetByteCount(manifestJson) > MaxContextManifestBytes)
        {
            // Genuinely unreachable with today's bounded manifest fields and the evidence
            // reader's own capture bound, but never silently truncated or persisted over the
            // bound if a future field addition regresses this.
            return Result<CreateClaudeCriticalReviewAttemptCommandResult>.Failure(
                Error.Failure("agent_attempts.context_manifest_too_large", "The context manifest exceeds its bound."));
        }

        var manifestPartialPath = artifactStore.GetPartialPath(run.Id, attemptId, ArtifactPurpose.AgentContextManifest);
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPartialPath)!);
        await File.WriteAllTextAsync(manifestPartialPath, manifestJson, cancellationToken);
        var sealedManifest = await artifactStore.SealAsync(run.Id, attemptId, ArtifactPurpose.AgentContextManifest, cancellationToken);
        if (sealedManifest is null)
        {
            return Result<CreateClaudeCriticalReviewAttemptCommandResult>.Failure(
                Error.Failure("agent_attempts.context_manifest_seal_failed", "The context manifest could not be sealed."));
        }

        var attemptNumber = await dbContext.Attempts.Where(candidate => candidate.RunId == run.Id).CountAsync(cancellationToken) + 1;

        var attempt = Attempt.ClaimAgentCriticalReview(
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
            nowUtc);
        dbContext.Attempts.Add(attempt);

        // The one authoritative record of which message this attempt reviews — sequence 0, the
        // attempt's only input. Never a second, competing column on Attempt itself.
        var inputMessage = AttemptInputMessage.Record(Guid.NewGuid(), attemptId, proposalMessage.Id, sequence: 0);
        dbContext.AttemptInputMessages.Add(inputMessage);

        var manifestArtifact = Artifact.Record(
            manifestArtifactId,
            run.Id,
            attemptId,
            ArtifactPurpose.AgentContextManifest,
            "application/json",
            sealedManifest.RelativeStoragePath,
            sealedManifest.ContentHash,
            sealedManifest.ByteLength,
            truncated: false,
            ArtifactCaptureOutcome.Captured,
            ArtifactSensitivity.HostConstructedContent,
            ArtifactRetentionPolicy.RetainUntilRunDeleted,
            nowUtc);
        dbContext.Artifacts.Add(manifestArtifact);

        if (run.Lifecycle == RunLifecycle.Created)
        {
            run.Claim(nowUtc);
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // The exception means this DbContext's change tracker no longer reliably reflects
            // what actually committed — the cause is never inferred from what this request
            // attempted, only from a fresh, untracked read of what the database actually holds
            // now.
            var thisAttemptPersisted = await dbContext.Attempts
                .AsNoTracking()
                .AnyAsync(candidate => candidate.Id == attemptId, cancellationToken);
            if (thisAttemptPersisted)
            {
                // Committed despite the thrown exception (e.g. the failure came from an
                // unrelated statement later in the same batch) — nothing to clean up, and
                // reporting a conflict or deleting this request's own artifact here would both
                // be false.
                return Result<CreateClaudeCriticalReviewAttemptCommandResult>.Success(
                    new CreateClaudeCriticalReviewAttemptCommandResult(attemptId, attemptNumber));
            }

            // Never persisted: the sealed manifest artifact this request already wrote is
            // certain to never be referenced by any Artifact row, so it is cleaned up
            // regardless of what actually caused the failure. The two entities this call added
            // are also removed from tracking so this DbContext instance can never later
            // accidentally re-attempt to persist a known-failed insert if its scope continues.
            artifactStore.DeleteOrphanedSealedFile(run.Id, attemptId, ArtifactPurpose.AgentContextManifest);
            dbContext.Attempts.Remove(attempt);
            dbContext.AttemptInputMessages.Remove(inputMessage);
            dbContext.Artifacts.Remove(manifestArtifact);

            var competingRunningAttemptExists = await dbContext.Attempts
                .AsNoTracking()
                .AnyAsync(candidate => candidate.RunId == run.Id && candidate.Status == AttemptStatus.Running, cancellationToken);
            if (competingRunningAttemptExists)
            {
                // The race the filtered unique index exists to close: a concurrent request
                // committed its own Running attempt for this run first.
                return Result<CreateClaudeCriticalReviewAttemptCommandResult>.Failure(
                    Error.Conflict("attempts.run_has_active_attempt", "This run already has an attempt in progress."));
            }

            // Not a race loss — no competing Running attempt exists for this run at all. Some
            // other persistence failure caused this (disk, corruption, an unrelated
            // constraint); never report a conflict that would falsely imply a race that never
            // happened.
            return Result<CreateClaudeCriticalReviewAttemptCommandResult>.Failure(
                Error.Failure("attempts.persistence_failed", "The attempt could not be durably recorded."));
        }

        return Result<CreateClaudeCriticalReviewAttemptCommandResult>.Success(
            new CreateClaudeCriticalReviewAttemptCommandResult(attempt.Id, attempt.AttemptNumber));
    }

    /// <summary>
    /// Validates every fact Section 3 requires about the reviewed message before this attempt is
    /// ever claimed: it belongs to this run, is a provider-observed Codex Proposal, its owning
    /// Codex planning attempt actually completed as Proposed, and that planning attempt is bound
    /// to the exact same workspace/checkpoint/fingerprint this critical-review attempt is about
    /// to claim — never a different or since-superseded checkpoint.
    /// </summary>
    private async Task<ReviewedProposalValidation> ValidateReviewedProposalAsync(
        Guid runId, Guid proposalMessageId, Guid workspaceId, GitCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        var message = await dbContext.CollaborationMessages
            .SingleOrDefaultAsync(candidate => candidate.Id == proposalMessageId, cancellationToken);
        if (message is null || message.RunId != runId)
        {
            return ReviewedProposalValidation.Failed(
                Error.NotFound("agent_attempts.proposal_not_found", "The requested proposal was not found for this run."));
        }

        if (message.Type != CollaborationMessageType.Proposal
            || message.Provenance != CollaborationMessageProvenance.ProviderObserved
            || message.AttemptId is null)
        {
            return ReviewedProposalValidation.Failed(
                Error.Conflict(
                    "agent_attempts.not_provider_observed_planner_proposal",
                    "Only a provider-observed Planner proposal can be requested for critical review."));
        }


        // Role-first, per ADR-0009: the owning attempt's AgentRole is the sole authority for
        // whether this message is a real Planner proposal. AgentProvider participates only as
        // provenance-integrity evidence inside this helper — never compared to a fixed provider to
        // authorize the role.
        var owningAttempt = await AgentAuthoredMessageEligibility.ResolveOwningAttemptAsync(
            dbContext, message, runId, AgentRole.Planner, cancellationToken);
        if (owningAttempt is null || owningAttempt.Status != AttemptStatus.Completed || owningAttempt.AgentOutcome != AgentOutcome.Proposed)
        {
            return ReviewedProposalValidation.Failed(
                Error.Conflict(
                    "agent_attempts.proposal_attempt_not_valid",
                    "The proposal's owning planning attempt did not complete as a valid proposal."));
        }

        if (owningAttempt.AgentGitWorkspaceId != workspaceId
            || owningAttempt.AgentGitCheckpointId != checkpoint.Id
            || !string.Equals(owningAttempt.AgentCheckpointFingerprintSha256, checkpoint.FingerprintSha256, StringComparison.Ordinal))
        {
            return ReviewedProposalValidation.Failed(
                Error.Conflict(
                    "agent_attempts.proposal_checkpoint_stale",
                    "The proposal was made against a different checkpoint than the one currently current for this workspace."));
        }

        return ReviewedProposalValidation.Succeeded(message);
    }

    private sealed record ReviewedProposalValidation(CollaborationMessage? Message, Error? Error)
    {
        public static ReviewedProposalValidation Succeeded(CollaborationMessage message) => new(message, null);

        public static ReviewedProposalValidation Failed(Error error) => new(null, error);
    }
}
