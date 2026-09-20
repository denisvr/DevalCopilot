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

namespace DevalCopilot.Application.Features.Runs.Commands.CreateImplementationAttempt;

/// <summary>
/// Claims one durable Claude implementation attempt. Mirrors
/// <c>CreateChallengeResolutionAttemptCommandHandler</c>'s workspace/lease/checkpoint eligibility
/// chain and sealed-manifest/persistence-race handling exactly, plus the additional resolved-plan
/// identity chain: exactly two eligible forms (an accepted original Proposal, or a resolved
/// revised Proposal), both bound to the run, workspace, and current checkpoint, and neither
/// already successfully implemented.
/// </summary>
public sealed class CreateImplementationAttemptCommandHandler(
    IDevalCopilotDbContext dbContext,
    IGitWorkspaceEvidenceReader evidenceReader,
    IArtifactStore artifactStore,
    TimeProvider timeProvider)
    : ICommandHandler<CreateImplementationAttemptCommand, Result<CreateImplementationAttemptCommandResult>>
{
    /// <summary>Hard ceiling on the sealed context-manifest artifact — a bounded reference
    /// document, never a transcript or repository copy.</summary>
    private const int MaxContextManifestBytes = 32 * 1024;

    /// <summary>Implementation is inherently multi-step (read, edit, re-read, verify its own
    /// work) — deliberately longer than the single-turn critical-review/resolution stages, but
    /// still a hard bound: cancellation and process-tree termination apply the same way once it
    /// elapses.</summary>
    private static readonly TimeSpan InvocationTimeout = TimeSpan.FromMinutes(20);
    private const int MaxBytesPerStream = 256 * 1024;
    private const int MaxTotalCapturedBytes = 512 * 1024;

    public async Task<Result<CreateImplementationAttemptCommandResult>> HandleAsync(
        CreateImplementationAttemptCommand command, CancellationToken cancellationToken)
    {
        var run = await dbContext.Runs.SingleOrDefaultAsync(candidate => candidate.Id == command.RunId, cancellationToken);
        if (run is null)
        {
            return Result<CreateImplementationAttemptCommandResult>.Failure(
                Error.NotFound("runs.not_found", "The requested run was not found."));
        }

        if (run.Lifecycle != RunLifecycle.Running)
        {
            return Result<CreateImplementationAttemptCommandResult>.Failure(
                Error.Conflict("runs.not_running", $"The run is {run.Lifecycle} and cannot start an implementation attempt."));
        }

        var alreadyRunning = await dbContext.Attempts.AnyAsync(
            candidate => candidate.RunId == run.Id && candidate.Status == AttemptStatus.Running,
            cancellationToken);
        if (alreadyRunning)
        {
            return Result<CreateImplementationAttemptCommandResult>.Failure(
                Error.Conflict("attempts.run_has_active_attempt", "This run already has an attempt in progress."));
        }

        var workspace = await dbContext.GitWorkspaces
            .Where(candidate => candidate.ProjectId == run.ProjectId)
            .OrderByDescending(candidate => candidate.WorkspaceNumber)
            .FirstOrDefaultAsync(cancellationToken);
        if (workspace is null || workspace.Status != WorkspaceStatus.Ready)
        {
            return Result<CreateImplementationAttemptCommandResult>.Failure(
                Error.Conflict("agent_attempts.workspace_not_ready", "A ready isolated workspace is required to request an implementation."));
        }

        var leaseIsActive = await dbContext.RepositoryMutationLeases
            .AnyAsync(lease => lease.WorkspaceId == workspace.Id && lease.Status == LeaseStatus.Active, cancellationToken);
        if (!leaseIsActive)
        {
            return Result<CreateImplementationAttemptCommandResult>.Failure(
                Error.Conflict("agent_attempts.lease_not_active", "An active workspace lease is required to request an implementation."));
        }

        var checkpoint = await dbContext.GitCheckpoints
            .Where(candidate => candidate.WorkspaceId == workspace.Id)
            .OrderByDescending(candidate => candidate.CheckpointNumber)
            .FirstOrDefaultAsync(cancellationToken);
        if (checkpoint is null)
        {
            return Result<CreateImplementationAttemptCommandResult>.Failure(
                Error.Conflict("agent_attempts.checkpoint_missing", "A current Git checkpoint is required to request an implementation."));
        }

        var claudeSnapshot = await dbContext.HostCapabilitySnapshots
            .SingleOrDefaultAsync(candidate => candidate.Capability == Capability.ClaudeCli, cancellationToken);
        if (claudeSnapshot is null || claudeSnapshot.ReasonCode != CapabilityProbeReason.None || string.IsNullOrWhiteSpace(claudeSnapshot.ResolvedExecutablePath))
        {
            return Result<CreateImplementationAttemptCommandResult>.Failure(
                Error.Conflict("agent_attempts.provider_not_observed", "The Claude runtime is not currently observed as available."));
        }

        var evidence = await evidenceReader.CaptureAsync(workspace.WorkspacePath, cancellationToken);
        if (evidence.Outcome != GitWorkspaceEvidenceOutcome.Success || evidence.FingerprintSha256 != checkpoint.FingerprintSha256)
        {
            return Result<CreateImplementationAttemptCommandResult>.Failure(
                Error.Conflict("agent_attempts.checkpoint_not_current", "The selected source checkpoint is no longer current for this workspace."));
        }

        var attemptId = Guid.NewGuid();

        var alreadyImplemented = await ImplementationInputIdentity.HasCompetingSuccessfulImplementationAsync(
            dbContext, run.Id, attemptId, command.PlanProposalMessageId, checkpoint.Id, cancellationToken);
        if (alreadyImplemented)
        {
            return Result<CreateImplementationAttemptCommandResult>.Failure(
                Error.Conflict("agent_attempts.already_implemented", "This resolved plan already has a successful implementation."));
        }

        var configuredVerificationCommands = await dbContext.VerificationCommands
            .Where(candidate => candidate.ProjectId == run.ProjectId)
            .Select(candidate => new ImplementationContextManifestBuilder.VerificationCommandReference(candidate.Name, candidate.IsEnabled))
            .ToListAsync(cancellationToken);

        var planValidation = await ValidateResolvedPlanAsync(
            run, command.PlanProposalMessageId, workspace, checkpoint, evidence, configuredVerificationCommands, cancellationToken);
        if (planValidation.Error is { } error)
        {
            return Result<CreateImplementationAttemptCommandResult>.Failure(error);
        }

        var (manifestJson, orderedInputMessageIds) = (planValidation.ManifestJson!, planValidation.OrderedInputMessageIds!);
        if (System.Text.Encoding.UTF8.GetByteCount(manifestJson) > MaxContextManifestBytes)
        {
            // Genuinely unreachable with today's bounded manifest fields and evidence reader's
            // own capture bound, but never silently truncated or persisted over the bound if a
            // future field addition regresses this.
            return Result<CreateImplementationAttemptCommandResult>.Failure(
                Error.Failure("agent_attempts.context_manifest_too_large", "The context manifest exceeds its bound."));
        }

        var manifestArtifactId = Guid.NewGuid();
        var nowUtc = timeProvider.GetUtcNow();

        var manifestPartialPath = artifactStore.GetPartialPath(run.Id, attemptId, ArtifactPurpose.AgentContextManifest);
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPartialPath)!);
        await File.WriteAllTextAsync(manifestPartialPath, manifestJson, cancellationToken);
        var sealedManifest = await artifactStore.SealAsync(run.Id, attemptId, ArtifactPurpose.AgentContextManifest, cancellationToken);
        if (sealedManifest is null)
        {
            return Result<CreateImplementationAttemptCommandResult>.Failure(
                Error.Failure("agent_attempts.context_manifest_seal_failed", "The context manifest could not be sealed."));
        }

        var attemptNumber = await dbContext.Attempts.Where(candidate => candidate.RunId == run.Id).CountAsync(cancellationToken) + 1;

        var attempt = Attempt.ClaimAgentImplementation(
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

        var inputMessages = new List<AttemptInputMessage>(orderedInputMessageIds.Count);
        for (var index = 0; index < orderedInputMessageIds.Count; index++)
        {
            inputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), attemptId, orderedInputMessageIds[index], sequence: index));
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
                return Result<CreateImplementationAttemptCommandResult>.Success(
                    new CreateImplementationAttemptCommandResult(attemptId, attemptNumber));
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
                return Result<CreateImplementationAttemptCommandResult>.Failure(
                    Error.Conflict("attempts.run_has_active_attempt", "This run already has an attempt in progress."));
            }

            return Result<CreateImplementationAttemptCommandResult>.Failure(
                Error.Failure("attempts.persistence_failed", "The attempt could not be durably recorded."));
        }

        return Result<CreateImplementationAttemptCommandResult>.Success(
            new CreateImplementationAttemptCommandResult(attempt.Id, attempt.AttemptNumber));
    }

    /// <summary>
    /// Determines which of the exactly two eligible resolved-plan forms applies to
    /// <paramref name="planProposalMessageId"/> — never both, never neither reconstructed from
    /// display text — validates every fact this slice requires about it, and builds the exact
    /// ordered input identity plus the sealed context-manifest content for it.
    /// </summary>
    private async Task<ResolvedPlanValidation> ValidateResolvedPlanAsync(
        Run run,
        Guid planProposalMessageId,
        GitWorkspace workspace,
        GitCheckpoint checkpoint,
        GitWorkspaceEvidenceResult evidence,
        IReadOnlyList<ImplementationContextManifestBuilder.VerificationCommandReference> configuredVerificationCommands,
        CancellationToken cancellationToken)
    {
        var proposalMessage = await dbContext.CollaborationMessages
            .SingleOrDefaultAsync(candidate => candidate.Id == planProposalMessageId, cancellationToken);
        if (proposalMessage is null || proposalMessage.RunId != run.Id || proposalMessage.Type != CollaborationMessageType.Proposal)
        {
            return ResolvedPlanValidation.Failed(
                Error.Conflict(
                    "agent_attempts.not_provider_observed_plan_proposal",
                    "The requested plan is not a provider-observed proposal."));
        }

        // Role-first, per ADR-0009: exactly two roles may ever own an eligible resolved-plan
        // Proposal (Planner's own original proposal, or Resolver's revised proposal) — AgentRole is
        // the sole authority for which, never AgentProvider. Both branches below share the same
        // provenance-integrity check (message.Actor must truthfully match the owning attempt's real
        // provider), performed once here rather than duplicated per branch.
        var owningAttempt = await dbContext.Attempts.SingleOrDefaultAsync(
            candidate => candidate.Id == proposalMessage.AttemptId, cancellationToken);
        if (owningAttempt is null
            || owningAttempt.RunId != run.Id
            || owningAttempt.Kind != AttemptKind.Agent
            || owningAttempt.Status != AttemptStatus.Completed
            || proposalMessage.Provenance != CollaborationMessageProvenance.ProviderObserved
            || owningAttempt.AgentProvider is not { } owningProvider
            || !Enum.IsDefined(owningProvider)
            || proposalMessage.Actor != AgentProviderParticipant.For(owningProvider))
        {
            return ResolvedPlanValidation.Failed(
                Error.Conflict("agent_attempts.proposal_attempt_not_valid", "The plan's owning attempt did not complete validly."));
        }

        if (owningAttempt.AgentGitWorkspaceId != workspace.Id
            || owningAttempt.AgentGitCheckpointId != checkpoint.Id
            || !string.Equals(owningAttempt.AgentCheckpointFingerprintSha256, checkpoint.FingerprintSha256, StringComparison.Ordinal))
        {
            return ResolvedPlanValidation.Failed(
                Error.Conflict(
                    "agent_attempts.proposal_checkpoint_stale",
                    "The plan was made against a different checkpoint than the one currently current for this workspace."));
        }

        if (owningAttempt.AgentRole == AgentRole.Planner
            && owningAttempt.AgentResponseContract == AgentAttemptContract.For(AgentRole.Planner).ResponseContract
            && owningAttempt.AgentOutcome == AgentOutcome.Proposed)
        {
            return await ValidateAcceptedOriginalProposalAsync(
                run, proposalMessage, workspace, checkpoint, evidence, configuredVerificationCommands, cancellationToken);
        }

        if (owningAttempt.AgentRole == AgentRole.Resolver
            && owningAttempt.AgentResponseContract == AgentAttemptContract.For(AgentRole.Resolver).ResponseContract
            && owningAttempt.AgentOutcome == AgentOutcome.Resolved)
        {
            return await ValidateResolvedRevisedProposalAsync(
                run, proposalMessage, owningAttempt, evidence, configuredVerificationCommands, cancellationToken);
        }

        return ResolvedPlanValidation.Failed(
            Error.Conflict(
                "agent_attempts.plan_not_resolved",
                "The requested plan is neither an accepted original proposal nor a resolved revised proposal."));
    }

    private async Task<ResolvedPlanValidation> ValidateAcceptedOriginalProposalAsync(
        Run run,
        CollaborationMessage proposalMessage,
        GitWorkspace workspace,
        GitCheckpoint checkpoint,
        GitWorkspaceEvidenceResult evidence,
        IReadOnlyList<ImplementationContextManifestBuilder.VerificationCommandReference> configuredVerificationCommands,
        CancellationToken cancellationToken)
    {
        var reviewAttempt = await dbContext.Attempts
            .Where(candidate =>
                candidate.RunId == run.Id
                && candidate.Kind == AttemptKind.Agent
                && candidate.AgentRole == AgentRole.CriticalReviewer
                && candidate.Status == AttemptStatus.Completed
                && candidate.AgentOutcome == AgentOutcome.Accepted)
            .Join(
                dbContext.AttemptInputMessages.Where(inputMessage => inputMessage.Sequence == 0 && inputMessage.CollaborationMessageId == proposalMessage.Id),
                candidate => candidate.Id,
                inputMessage => inputMessage.AttemptId,
                (candidate, _) => candidate)
            .OrderBy(candidate => candidate.AttemptNumber)
            .FirstOrDefaultAsync(cancellationToken);
        if (reviewAttempt is null || reviewAttempt.AgentProvider is not { } reviewProvider || !Enum.IsDefined(reviewProvider))
        {
            return ResolvedPlanValidation.Failed(
                Error.Conflict(
                    "agent_attempts.no_accepted_review_found",
                    "No completed, successful Claude acceptance review was found for this proposal."));
        }

        if (reviewAttempt.AgentGitWorkspaceId != workspace.Id
            || reviewAttempt.AgentGitCheckpointId != checkpoint.Id
            || !string.Equals(reviewAttempt.AgentCheckpointFingerprintSha256, checkpoint.FingerprintSha256, StringComparison.Ordinal))
        {
            return ResolvedPlanValidation.Failed(
                Error.Conflict(
                    "agent_attempts.review_checkpoint_stale",
                    "The acceptance review was made against a different checkpoint than the one currently current for this workspace."));
        }

        // reviewAttempt's own role (CriticalReviewer) and provider (present and defined) were
        // already validated above — provenance integrity here means the Acceptance's own recorded
        // Actor must still truthfully match that SAME already-validated attempt's provider, never a
        // fixed literal.
        var expectedAcceptanceActor = AgentProviderParticipant.For(reviewProvider);
        var acceptanceMessage = await dbContext.CollaborationMessages.SingleOrDefaultAsync(
            candidate =>
                candidate.AttemptId == reviewAttempt.Id
                && candidate.Type == CollaborationMessageType.Acceptance
                && candidate.InReplyToMessageId == proposalMessage.Id,
            cancellationToken);
        if (acceptanceMessage is null
            || acceptanceMessage.Provenance != CollaborationMessageProvenance.ProviderObserved
            || acceptanceMessage.Actor != expectedAcceptanceActor)
        {
            return ResolvedPlanValidation.Failed(
                Error.Conflict("agent_attempts.acceptance_not_valid", "The acceptance evidence for this proposal is not a valid provider-observed record."));
        }

        var manifestJson = ImplementationContextManifestBuilder.BuildForAcceptedOriginalProposal(
            run.ProjectId,
            workspace.Id,
            checkpoint.Id,
            checkpoint.FingerprintSha256,
            run.Objective,
            proposalMessage.Id,
            proposalMessage.Summary,
            proposalMessage.StructuredContentJson,
            new ImplementationContextManifestBuilder.AcceptanceEvidence(acceptanceMessage.Summary, acceptanceMessage.StructuredContentJson),
            evidence.ChangedPaths,
            evidence.CompleteDiff,
            configuredVerificationCommands);

        return ResolvedPlanValidation.Succeeded(manifestJson, [proposalMessage.Id, acceptanceMessage.Id]);
    }

    private async Task<ResolvedPlanValidation> ValidateResolvedRevisedProposalAsync(
        Run run,
        CollaborationMessage revisedProposalMessage,
        Attempt resolverAttempt,
        GitWorkspaceEvidenceResult evidence,
        IReadOnlyList<ImplementationContextManifestBuilder.VerificationCommandReference> configuredVerificationCommands,
        CancellationToken cancellationToken)
    {
        // resolverAttempt's own role (Resolver) was already validated by the caller, but its
        // provider is re-verified present and defined here independently rather than trusted
        // across the method boundary — provenance integrity means each Decision's own recorded
        // Actor must still truthfully match that SAME attempt's provider, never a fixed literal.
        if (resolverAttempt.AgentProvider is not { } resolverProvider || !Enum.IsDefined(resolverProvider))
        {
            return ResolvedPlanValidation.Failed(
                Error.Conflict(
                    "agent_attempts.decisions_not_valid",
                    "The resolver's decision set is not a valid, complete, provider-observed set."));
        }

        var expectedDecisionActor = AgentProviderParticipant.For(resolverProvider);
        var orderedDecisions = await dbContext.CollaborationMessages
            .Where(candidate => candidate.AttemptId == resolverAttempt.Id && candidate.Type == CollaborationMessageType.Decision)
            .OrderBy(candidate => candidate.Sequence)
            .ToListAsync(cancellationToken);
        if (orderedDecisions.Count == 0
            || orderedDecisions.Any(decision => decision.Provenance != CollaborationMessageProvenance.ProviderObserved || decision.Actor != expectedDecisionActor))
        {
            return ResolvedPlanValidation.Failed(
                Error.Conflict(
                    "agent_attempts.decisions_not_valid",
                    "The resolver's decision set is not a valid, complete, provider-observed set."));
        }

        var manifestJson = ImplementationContextManifestBuilder.BuildForResolvedRevisedProposal(
            run.ProjectId,
            resolverAttempt.AgentGitWorkspaceId!.Value,
            resolverAttempt.AgentGitCheckpointId!.Value,
            resolverAttempt.AgentCheckpointFingerprintSha256!,
            run.Objective,
            revisedProposalMessage.Id,
            revisedProposalMessage.Summary,
            revisedProposalMessage.StructuredContentJson,
            orderedDecisions
                .Select(decision => new ImplementationContextManifestBuilder.DecisionEvidence(
                    decision.InReplyToMessageId!.Value, decision.Summary, decision.StructuredContentJson))
                .ToArray(),
            evidence.ChangedPaths,
            evidence.CompleteDiff,
            configuredVerificationCommands);

        var orderedInputMessageIds = new List<Guid> { revisedProposalMessage.Id };
        orderedInputMessageIds.AddRange(orderedDecisions.Select(decision => decision.Id));

        return ResolvedPlanValidation.Succeeded(manifestJson, orderedInputMessageIds);
    }

    private sealed record ResolvedPlanValidation(string? ManifestJson, IReadOnlyList<Guid>? OrderedInputMessageIds, Error? Error)
    {
        public static ResolvedPlanValidation Succeeded(string manifestJson, IReadOnlyList<Guid> orderedInputMessageIds) =>
            new(manifestJson, orderedInputMessageIds, null);

        public static ResolvedPlanValidation Failed(Error error) => new(null, null, error);
    }
}
