using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Commands.CreateChallengeResolutionAttempt;

/// <summary>
/// Claims one durable Codex challenge-resolution attempt. Mirrors
/// <c>CreateClaudeCriticalReviewAttemptCommandHandler</c>'s workspace/lease/checkpoint
/// eligibility chain and sealed-manifest/persistence-race handling exactly, plus the additional
/// chain of checks that the named Challenged review attempt, its original Proposal, and its
/// complete Challenge set are all real, current, mutually consistent, and not already
/// successfully resolved.
/// </summary>
public sealed class CreateChallengeResolutionAttemptCommandHandler(
    IDevalCopilotDbContext dbContext,
    IGitWorkspaceEvidenceReader evidenceReader,
    IArtifactStore artifactStore,
    TimeProvider timeProvider)
    : ICommandHandler<CreateChallengeResolutionAttemptCommand, Result<CreateChallengeResolutionAttemptCommandResult>>
{
    /// <summary>Hard ceiling on the sealed context-manifest artifact — a bounded reference
    /// document, never a transcript or repository copy.</summary>
    private const int MaxContextManifestBytes = 32 * 1024;

    private static readonly TimeSpan InvocationTimeout = TimeSpan.FromMinutes(10);
    private const int MaxBytesPerStream = 256 * 1024;
    private const int MaxTotalCapturedBytes = 512 * 1024;

    public async Task<Result<CreateChallengeResolutionAttemptCommandResult>> HandleAsync(
        CreateChallengeResolutionAttemptCommand command, CancellationToken cancellationToken)
    {
        var run = await dbContext.Runs.SingleOrDefaultAsync(candidate => candidate.Id == command.RunId, cancellationToken);
        if (run is null)
        {
            return Result<CreateChallengeResolutionAttemptCommandResult>.Failure(
                Error.NotFound("runs.not_found", "The requested run was not found."));
        }

        if (run.Lifecycle != RunLifecycle.Running)
        {
            return Result<CreateChallengeResolutionAttemptCommandResult>.Failure(
                Error.Conflict("runs.not_running", $"The run is {run.Lifecycle} and cannot start a challenge-resolution attempt."));
        }

        var alreadyRunning = await dbContext.Attempts.AnyAsync(
            candidate => candidate.RunId == run.Id && candidate.Status == AttemptStatus.Running,
            cancellationToken);
        if (alreadyRunning)
        {
            return Result<CreateChallengeResolutionAttemptCommandResult>.Failure(
                Error.Conflict("attempts.run_has_active_attempt", "This run already has an attempt in progress."));
        }

        var workspace = await dbContext.GitWorkspaces
            .Where(candidate => candidate.ProjectId == run.ProjectId)
            .OrderByDescending(candidate => candidate.WorkspaceNumber)
            .FirstOrDefaultAsync(cancellationToken);
        if (workspace is null || workspace.Status != WorkspaceStatus.Ready)
        {
            return Result<CreateChallengeResolutionAttemptCommandResult>.Failure(
                Error.Conflict("agent_attempts.workspace_not_ready", "A ready isolated workspace is required to request a challenge resolution."));
        }

        var leaseIsActive = await dbContext.RepositoryMutationLeases
            .AnyAsync(lease => lease.WorkspaceId == workspace.Id && lease.Status == LeaseStatus.Active, cancellationToken);
        if (!leaseIsActive)
        {
            return Result<CreateChallengeResolutionAttemptCommandResult>.Failure(
                Error.Conflict("agent_attempts.lease_not_active", "An active workspace lease is required to request a challenge resolution."));
        }

        var checkpoint = await dbContext.GitCheckpoints
            .Where(candidate => candidate.WorkspaceId == workspace.Id)
            .OrderByDescending(candidate => candidate.CheckpointNumber)
            .FirstOrDefaultAsync(cancellationToken);
        if (checkpoint is null)
        {
            return Result<CreateChallengeResolutionAttemptCommandResult>.Failure(
                Error.Conflict("agent_attempts.checkpoint_missing", "A current Git checkpoint is required to request a challenge resolution."));
        }

        var codexSnapshot = await dbContext.HostCapabilitySnapshots
            .SingleOrDefaultAsync(candidate => candidate.Capability == Capability.CodexCli, cancellationToken);
        if (codexSnapshot is null || codexSnapshot.ReasonCode != CapabilityProbeReason.None || string.IsNullOrWhiteSpace(codexSnapshot.ResolvedExecutablePath))
        {
            return Result<CreateChallengeResolutionAttemptCommandResult>.Failure(
                Error.Conflict("agent_attempts.provider_not_observed", "The Codex runtime is not currently observed as available."));
        }

        var evidence = await evidenceReader.CaptureAsync(workspace.WorkspacePath, cancellationToken);
        if (evidence.Outcome != GitWorkspaceEvidenceOutcome.Success || evidence.FingerprintSha256 != checkpoint.FingerprintSha256)
        {
            return Result<CreateChallengeResolutionAttemptCommandResult>.Failure(
                Error.Conflict("agent_attempts.checkpoint_not_current", "The selected source checkpoint is no longer current for this workspace."));
        }

        var challengeValidation = await ValidateChallengedReviewAsync(run.Id, command.ChallengedReviewAttemptId, workspace.Id, checkpoint, cancellationToken);
        if (challengeValidation.Error is { } error)
        {
            return Result<CreateChallengeResolutionAttemptCommandResult>.Failure(error);
        }

        var (originalProposalMessage, orderedChallenges) = (challengeValidation.OriginalProposal!, challengeValidation.OrderedChallenges!);
        var challengeMessageIds = orderedChallenges.Select(challenge => challenge.Id).ToList();

        var alreadyResolved = await dbContext.Attempts
            .Where(candidate =>
                candidate.Kind == AttemptKind.Agent
                && candidate.AgentProvider == AgentProvider.Codex
                && candidate.AgentRole == AgentRole.Resolver
                && candidate.AgentOutcome == AgentOutcome.Resolved)
            .Join(
                dbContext.AttemptInputMessages.Where(inputMessage => challengeMessageIds.Contains(inputMessage.CollaborationMessageId)),
                candidate => candidate.Id,
                inputMessage => inputMessage.AttemptId,
                (candidate, inputMessage) => candidate)
            .AnyAsync(cancellationToken);
        if (alreadyResolved)
        {
            return Result<CreateChallengeResolutionAttemptCommandResult>.Failure(
                Error.Conflict("agent_attempts.already_resolved", "This challenged review already has a successful resolution."));
        }

        var attemptId = Guid.NewGuid();
        var manifestArtifactId = Guid.NewGuid();
        var nowUtc = timeProvider.GetUtcNow();

        var manifestJson = ChallengeResolutionContextManifestBuilder.Build(
            run.ProjectId,
            workspace.Id,
            checkpoint.Id,
            checkpoint.FingerprintSha256,
            run.Objective,
            originalProposalMessage.Id,
            originalProposalMessage.Summary,
            originalProposalMessage.StructuredContentJson,
            orderedChallenges
                .Select(challenge => new ChallengeResolutionContextManifestBuilder.ChallengeEvidence(
                    challenge.Id, challenge.Summary, challenge.StructuredContentJson))
                .ToArray(),
            evidence.ChangedPaths,
            evidence.CompleteDiff);
        if (System.Text.Encoding.UTF8.GetByteCount(manifestJson) > MaxContextManifestBytes)
        {
            // Genuinely unreachable with today's bounded manifest fields, the fixed maximum
            // Challenge cardinality, and the evidence reader's own capture bound, but never
            // silently truncated or persisted over the bound if a future field addition
            // regresses this.
            return Result<CreateChallengeResolutionAttemptCommandResult>.Failure(
                Error.Failure("agent_attempts.context_manifest_too_large", "The context manifest exceeds its bound."));
        }

        var manifestPartialPath = artifactStore.GetPartialPath(run.Id, attemptId, ArtifactPurpose.AgentContextManifest);
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPartialPath)!);
        await File.WriteAllTextAsync(manifestPartialPath, manifestJson, cancellationToken);
        var sealedManifest = await artifactStore.SealAsync(run.Id, attemptId, ArtifactPurpose.AgentContextManifest, cancellationToken);
        if (sealedManifest is null)
        {
            return Result<CreateChallengeResolutionAttemptCommandResult>.Failure(
                Error.Failure("agent_attempts.context_manifest_seal_failed", "The context manifest could not be sealed."));
        }

        var attemptNumber = await dbContext.Attempts.Where(candidate => candidate.RunId == run.Id).CountAsync(cancellationToken) + 1;

        var attempt = Attempt.ClaimAgentChallengeResolution(
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

        // The one authoritative record of this attempt's exact ordered input set: the original
        // Proposal at sequence 0, then every Challenge in timeline order — never a second,
        // competing column on Attempt itself.
        var inputMessages = new List<AttemptInputMessage>(orderedChallenges.Count + 1)
        {
            AttemptInputMessage.Record(Guid.NewGuid(), attemptId, originalProposalMessage.Id, sequence: 0),
        };
        for (var index = 0; index < orderedChallenges.Count; index++)
        {
            inputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), attemptId, orderedChallenges[index].Id, sequence: index + 1));
        }

        dbContext.AttemptInputMessages.AddRange(inputMessages);

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
                return Result<CreateChallengeResolutionAttemptCommandResult>.Success(
                    new CreateChallengeResolutionAttemptCommandResult(attemptId, attemptNumber));
            }

            artifactStore.DeleteOrphanedSealedFile(run.Id, attemptId, ArtifactPurpose.AgentContextManifest);
            dbContext.Attempts.Remove(attempt);
            foreach (var inputMessage in inputMessages)
            {
                dbContext.AttemptInputMessages.Remove(inputMessage);
            }

            dbContext.Artifacts.Remove(manifestArtifact);

            var competingRunningAttemptExists = await dbContext.Attempts
                .AsNoTracking()
                .AnyAsync(candidate => candidate.RunId == run.Id && candidate.Status == AttemptStatus.Running, cancellationToken);
            if (competingRunningAttemptExists)
            {
                return Result<CreateChallengeResolutionAttemptCommandResult>.Failure(
                    Error.Conflict("attempts.run_has_active_attempt", "This run already has an attempt in progress."));
            }

            return Result<CreateChallengeResolutionAttemptCommandResult>.Failure(
                Error.Failure("attempts.persistence_failed", "The attempt could not be durably recorded."));
        }

        return Result<CreateChallengeResolutionAttemptCommandResult>.Success(
            new CreateChallengeResolutionAttemptCommandResult(attempt.Id, attempt.AttemptNumber));
    }

    /// <summary>
    /// Validates every fact this slice requires about the named Challenged review attempt before
    /// this resolution attempt is ever claimed: it belongs to this run, is a completed,
    /// successful ClaudeCode critical-review attempt bound to the exact same
    /// workspace/checkpoint this resolution attempt is about to claim; its original Proposal is a
    /// real, provider-observed Codex Proposal, itself bound to that same workspace/checkpoint;
    /// and its complete Challenge set is a non-empty, provider-observed Claude set that all
    /// replies to that exact Proposal.
    /// </summary>
    private async Task<ChallengedReviewValidation> ValidateChallengedReviewAsync(
        Guid runId, Guid challengedReviewAttemptId, Guid workspaceId, GitCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        var challengedReviewAttempt = await dbContext.Attempts
            .SingleOrDefaultAsync(candidate => candidate.Id == challengedReviewAttemptId, cancellationToken);
        if (challengedReviewAttempt is null || challengedReviewAttempt.RunId != runId)
        {
            return ChallengedReviewValidation.Failed(
                Error.NotFound("agent_attempts.challenged_review_not_found", "The requested challenged review attempt was not found for this run."));
        }

        if (challengedReviewAttempt.Kind != AttemptKind.Agent
            || challengedReviewAttempt.AgentProvider != AgentProvider.ClaudeCode
            || challengedReviewAttempt.AgentRole != AgentRole.CriticalReviewer
            || challengedReviewAttempt.Status != AttemptStatus.Completed
            || challengedReviewAttempt.AgentOutcome != AgentOutcome.Challenged)
        {
            return ChallengedReviewValidation.Failed(
                Error.Conflict(
                    "agent_attempts.challenged_review_not_valid",
                    "The requested attempt did not complete as a successful, challenged Claude critical review."));
        }

        if (challengedReviewAttempt.AgentGitWorkspaceId != workspaceId
            || challengedReviewAttempt.AgentGitCheckpointId != checkpoint.Id
            || !string.Equals(challengedReviewAttempt.AgentCheckpointFingerprintSha256, checkpoint.FingerprintSha256, StringComparison.Ordinal))
        {
            return ChallengedReviewValidation.Failed(
                Error.Conflict(
                    "agent_attempts.challenged_review_checkpoint_stale",
                    "The challenged review was made against a different checkpoint than the one currently current for this workspace."));
        }

        var originalProposalMessageId = await dbContext.AttemptInputMessages
            .Where(inputMessage => inputMessage.AttemptId == challengedReviewAttempt.Id && inputMessage.Sequence == 0)
            .Select(inputMessage => inputMessage.CollaborationMessageId)
            .SingleAsync(cancellationToken);

        var originalProposalMessage = await dbContext.CollaborationMessages
            .SingleOrDefaultAsync(candidate => candidate.Id == originalProposalMessageId, cancellationToken);
        if (originalProposalMessage is null
            || originalProposalMessage.Type != CollaborationMessageType.Proposal
            || originalProposalMessage.Provenance != CollaborationMessageProvenance.ProviderObserved
            || originalProposalMessage.Actor != ParticipantKind.Codex
            || originalProposalMessage.AttemptId is not { } owningProposalAttemptId)
        {
            return ChallengedReviewValidation.Failed(
                Error.Conflict(
                    "agent_attempts.not_provider_observed_codex_proposal",
                    "The challenged review's original proposal is not a provider-observed Codex proposal."));
        }

        var owningProposalAttempt = await dbContext.Attempts
            .SingleOrDefaultAsync(candidate => candidate.Id == owningProposalAttemptId, cancellationToken);
        if (owningProposalAttempt is null
            || owningProposalAttempt.Kind != AttemptKind.Agent
            || owningProposalAttempt.AgentProvider != AgentProvider.Codex
            || owningProposalAttempt.AgentRole != AgentRole.Planner
            || owningProposalAttempt.Status != AttemptStatus.Completed
            || owningProposalAttempt.AgentOutcome != AgentOutcome.Proposed)
        {
            return ChallengedReviewValidation.Failed(
                Error.Conflict(
                    "agent_attempts.proposal_attempt_not_valid",
                    "The original proposal's owning planning attempt did not complete as a valid proposal."));
        }

        if (owningProposalAttempt.AgentGitWorkspaceId != workspaceId
            || owningProposalAttempt.AgentGitCheckpointId != checkpoint.Id
            || !string.Equals(owningProposalAttempt.AgentCheckpointFingerprintSha256, checkpoint.FingerprintSha256, StringComparison.Ordinal))
        {
            return ChallengedReviewValidation.Failed(
                Error.Conflict(
                    "agent_attempts.proposal_checkpoint_stale",
                    "The original proposal was made against a different checkpoint than the one currently current for this workspace."));
        }

        var orderedChallenges = await dbContext.CollaborationMessages
            .Where(candidate => candidate.AttemptId == challengedReviewAttempt.Id && candidate.Type == CollaborationMessageType.Challenge)
            .OrderBy(candidate => candidate.Sequence)
            .ToListAsync(cancellationToken);
        if (orderedChallenges.Count == 0
            || orderedChallenges.Any(challenge =>
                challenge.Provenance != CollaborationMessageProvenance.ProviderObserved
                || challenge.Actor != ParticipantKind.Claude
                || challenge.InReplyToMessageId != originalProposalMessage.Id))
        {
            return ChallengedReviewValidation.Failed(
                Error.Conflict(
                    "agent_attempts.challenges_not_valid",
                    "The challenged review's challenge set is not a valid, complete, provider-observed set replying to its original proposal."));
        }

        return ChallengedReviewValidation.Succeeded(originalProposalMessage, orderedChallenges);
    }

    private sealed record ChallengedReviewValidation(
        CollaborationMessage? OriginalProposal, IReadOnlyList<CollaborationMessage>? OrderedChallenges, Error? Error)
    {
        public static ChallengedReviewValidation Succeeded(CollaborationMessage originalProposal, IReadOnlyList<CollaborationMessage> orderedChallenges) =>
            new(originalProposal, orderedChallenges, null);

        public static ChallengedReviewValidation Failed(Error error) => new(null, null, error);
    }
}
