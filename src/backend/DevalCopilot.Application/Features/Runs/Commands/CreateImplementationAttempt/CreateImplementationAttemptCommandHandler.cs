using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Policies;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Commands.CreateImplementationAttempt;

/// <summary>
/// Claims one durable Claude implementation attempt with immutable assignment facts. Mirrors
/// <c>CreateChallengeResolutionAttemptCommandHandler</c>'s workspace/lease/checkpoint eligibility
/// chain and sealed-manifest/persistence-race handling exactly, plus the additional resolved-plan
/// identity chain: exactly two eligible forms (an accepted original Proposal, or a first revised
/// Proposal that no later review challenged, bound to its exact Acceptance if it was re-reviewed and
/// accepted), both bound to the run, workspace, and current checkpoint, and neither
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
    private const string AdapterContractVersion = ClaudeMutationAdapterContract.ImplementationV2;
    private const string PlanChallengedCode = "agent_attempts.plan_challenged";

    /// <summary>Implementation is inherently multi-step (read, edit, re-read, verify its own
    /// work) — deliberately longer than the single-turn critical-review/resolution stages, but
    /// still a hard bound: cancellation and process-tree termination apply the same way once it
    /// elapses.</summary>
    private static readonly TimeSpan InvocationTimeout = AgentClaimPathPolicy.GetInvocationTimeout(AgentClaimPath.Implementation);
    private const int MaxBytesPerStream = 256 * 1024;
    private const int MaxTotalCapturedBytes = 512 * 1024;

    public async Task<Result<CreateImplementationAttemptCommandResult>> HandleAsync(
        CreateImplementationAttemptCommand command, CancellationToken cancellationToken)
    {
        // Supplied direct guidance is normalized (and refused if invalid) before any read or external work. The
        // validator normally rejects it first; this keeps a direct handler call equally safe. Null stays unguided.
        string? directGuidance = null;
        if (command.Guidance is not null)
        {
            directGuidance = DirectHumanGuidance.Normalize(command.Guidance);
            if (directGuidance is null)
            {
                return Result<CreateImplementationAttemptCommandResult>.Failure(DirectHumanGuidanceErrors.Invalid());
            }
        }

        var run = await dbContext.Runs.SingleOrDefaultAsync(candidate => candidate.Id == command.RunId, cancellationToken);
        if (run is null)
        {
            return Result<CreateImplementationAttemptCommandResult>.Failure(
                Error.NotFound("runs.not_found", "The requested run was not found."));
        }

        // The execution mode is a durable, immutable admission fact: read afresh (never from the tracked Run)
        // and checked before any workspace, evidence, manifest, or provider work.
        var executionModeError = await CurrentRunExecutionMode.CheckAgentAdmittedAsync(dbContext, run.Id, cancellationToken);
        if (executionModeError is not null)
        {
            return Result<CreateImplementationAttemptCommandResult>.Failure(executionModeError);
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

        // The run-wide Agent claim budget: every claimed Agent attempt, regardless of role,
        // provider, dispatch, result, or interruption, permanently consumes one slot. Checked
        // before any provider-availability probe or evidence capture so an exhausted run never
        // invokes a provider. The filtered unique index on (RunId, AgentBudgetSlot) is the
        // database backstop for the race this count-and-compare check alone cannot close.
        var agentAttemptsUsed = await dbContext.Attempts.CountAsync(
            candidate => candidate.RunId == run.Id && candidate.Kind == AttemptKind.Agent,
            cancellationToken);
        if (agentAttemptsUsed >= run.MaximumAgentAttempts)
        {
            return Result<CreateImplementationAttemptCommandResult>.Failure(
                Error.Conflict("agent_attempts.budget_exhausted", "This run has reached its maximum claimed Agent attempts."));
        }

        // The independent run-wide Agent invocation-TIME budget (see the companion ADR to
        // ADR-0012), enforced alongside — never instead of — the count-based budget above, at the
        // same point: before any provider-availability probe or evidence capture. A run with no
        // time-budget policy (a historical Run predating this decision) skips this check entirely
        // rather than being bound by a fabricated ceiling.
        if (run.MaximumAgentInvocationTime is { } maximumAgentInvocationTime)
        {
            var reservedAgentInvocationTime = await AgentInvocationTimeBudget.ComputeReservedAsync(dbContext, run.Id, asNoTracking: false, cancellationToken);
            if (reservedAgentInvocationTime is null)
            {
                return Result<CreateImplementationAttemptCommandResult>.Failure(
                    Error.Failure("agent_attempts.time_budget_evidence_invalid", "This claim's reserved Agent invocation time could not be safely evaluated: prior Agent attempt invocation-time evidence is missing or invalid, or this claim's own reservation is not representable."));
            }

            var projectedAgentInvocationTime = AgentInvocationTimeReservation.ComputeProjectedReservation(reservedAgentInvocationTime.Value, InvocationTimeout);
            if (projectedAgentInvocationTime is null)
            {
                return Result<CreateImplementationAttemptCommandResult>.Failure(
                    Error.Failure("agent_attempts.time_budget_evidence_invalid", "This claim's reserved Agent invocation time could not be safely evaluated: prior Agent attempt invocation-time evidence is missing or invalid, or this claim's own reservation is not representable."));
            }

            if (projectedAgentInvocationTime.Value > maximumAgentInvocationTime)
            {
                return Result<CreateImplementationAttemptCommandResult>.Failure(
                    Error.Conflict("agent_attempts.time_budget_exceeded", "This run has reached its maximum reserved Agent invocation time."));
            }
        }

        // The run-scoped, provider-separated token-activity stop (see AgentTokenStopGate), after
        // the run-wide budgets above and before any provider-availability probe, Git work, or
        // manifest sealing. Unconfigured, it reads nothing and changes nothing.
        var tokenStopError = await AgentTokenStopGate.CheckClaimAsync(dbContext, run, AgentProvider.ClaudeCode, cancellationToken);
        if (tokenStopError is not null)
        {
            return Result<CreateImplementationAttemptCommandResult>.Failure(tokenStopError);
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

        // A revised plan's lineage refusal (end of lineage, a challenging review, corrupt or unreadable
        // evidence, an unsupported role/provider pair) is decided from durable identity here, before any
        // provider probe, Git capture, or manifest sealing, so it does no external work. The original
        // Planner path and every other refusal keep their position after the checks below.
        var earlyRefusal = await EarlyRevisionRefusalAsync(run, command.PlanProposalMessageId, workspace, checkpoint, cancellationToken);
        if (earlyRefusal is not null)
        {
            return Result<CreateImplementationAttemptCommandResult>.Failure(earlyRefusal);
        }

        var claudeSnapshot = await dbContext.HostCapabilitySnapshots
            .SingleOrDefaultAsync(candidate => candidate.Capability == Capability.ClaudeCli, cancellationToken);
        if (claudeSnapshot is null || claudeSnapshot.ReasonCode != CapabilityProbeReason.None || string.IsNullOrWhiteSpace(claudeSnapshot.ResolvedExecutablePath))
        {
            return Result<CreateImplementationAttemptCommandResult>.Failure(
                Error.Conflict("agent_attempts.provider_not_observed", "The Claude runtime is not currently observed as available."));
        }

        var evidence = await evidenceReader.CaptureWithUntrackedPreviewsAsync(workspace.WorkspacePath, cancellationToken);
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
            run, command.PlanProposalMessageId, workspace, checkpoint, evidence, configuredVerificationCommands, directGuidance, cancellationToken);
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

        // The last read before the durable commit: the resolved plan's eligibility (including a
        // review that challenged it in the meantime), any competing implementation, and the run-wide
        // Running slot are decided again after the external work above. A plan that stopped being
        // eligible is refused here and its sealed manifest removed, so nothing that could mutate the
        // workspace is ever committed for it. The filtered unique index on (RunId WHERE Status =
        // 'Running') still backstops the commit itself.
        var lateValidation = await ValidateResolvedPlanAsync(
            run, command.PlanProposalMessageId, workspace, checkpoint, evidence, configuredVerificationCommands, directGuidance, cancellationToken);
        Error? lateError = lateValidation.Error;
        if (lateError is null && !lateValidation.OrderedInputMessageIds!.SequenceEqual(orderedInputMessageIds))
        {
            lateError = Error.Conflict("agent_attempts.plan_changed", "The resolved plan's evidence changed while this claim was being prepared.");
        }

        if (lateError is null
            && await ImplementationInputIdentity.HasCompetingSuccessfulImplementationAsync(
                dbContext, run.Id, attemptId, command.PlanProposalMessageId, checkpoint.Id, cancellationToken))
        {
            lateError = Error.Conflict("agent_attempts.already_implemented", "This resolved plan already has a successful implementation.");
        }

        if (lateError is null
            && await dbContext.Attempts.AsNoTracking().AnyAsync(
                candidate => candidate.RunId == run.Id && candidate.Status == AttemptStatus.Running, cancellationToken))
        {
            lateError = Error.Conflict("attempts.run_has_active_attempt", "This run already has an attempt in progress.");
        }

        if (lateError is not null)
        {
            artifactStore.DeleteOrphanedSealedFile(run.Id, attemptId, ArtifactPurpose.AgentContextManifest);
            return Result<CreateImplementationAttemptCommandResult>.Failure(lateError);
        }

        var attemptNumber = await dbContext.Attempts.Where(candidate => candidate.RunId == run.Id).CountAsync(cancellationToken) + 1;
        var agentBudgetSlot = agentAttemptsUsed + 1;

        // The execution mode is read afresh and guarded by its concurrency token, like the model request: the
        // single SaveChangesAsync below commits only while the stored mode is still the one decided against.
        var guardedModeError = await CurrentRunExecutionMode.ReadAndGuardAgentAsync(dbContext, run, cancellationToken);
        if (guardedModeError is not null)
        {
            artifactStore.DeleteOrphanedSealedFile(run.Id, attemptId, ArtifactPurpose.AgentContextManifest);
            return Result<CreateImplementationAttemptCommandResult>.Failure(guardedModeError);
        }

        // Read as late as possible (after every external step) and guarded by the Run's
        // concurrency token, so this Attempt snapshots exactly the model and effort pair that is still current
        // when the single SaveChangesAsync below commits — never an earlier-loaded, stale value.
        var requestedClaude = await CurrentClaudeModelPreference.ReadAndGuardAsync(dbContext, run, cancellationToken);

        // The Claude agentic-turn-limit request gets the same late read and commit-time guard; a stored
        // value outside the accepted range is refused here (with the sealed manifest removed) rather
        // than clamped or claimed without its limit.
        var requestedTurnLimit = await CurrentClaudeMutationTurnLimit.ReadAndGuardAsync(dbContext, run, cancellationToken);
        if (requestedTurnLimit.IsFailure)
        {
            artifactStore.DeleteOrphanedSealedFile(run.Id, attemptId, ArtifactPurpose.AgentContextManifest);
            return Result<CreateImplementationAttemptCommandResult>.Failure(requestedTurnLimit.Errors[0]);
        }

        // The token stop's own commit-time guard: the claim's Run UPDATE also requires the exact stop
        // policy this claim decided against (see CurrentTokenStopPolicy).
        CurrentTokenStopPolicy.Guard(dbContext, run);

        var attempt = Attempt.ClaimAgentImplementationWithAssignment(
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
            requestedModel: requestedClaude.Model,
            requestedEffort: requestedClaude.Effort,
            AgentPermissionProfile.WorkspaceEditOnly,
            AdapterContractVersion,
            agentBudgetSlot,
            requestedTurnLimit.Value,
            directGuidance);
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
        catch (DbUpdateException exception)
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

            if (exception is DbUpdateConcurrencyException)
            {
                // The Run's lifecycle or Claude model request changed between this claim's late
                // read and its commit (see CurrentClaudeModelPreference); the whole batch rolled
                // back, so nothing persisted and a retry re-reads the current state.
                // A token stop policy change is named as such; anything else keeps the generic
                // run-changed conflict.
                return Result<CreateImplementationAttemptCommandResult>.Failure(
                    await CurrentRunExecutionMode.HasChangedAsync(dbContext, run, cancellationToken)
                        ? CurrentRunExecutionMode.ChangedDuringClaim()
                        : await CurrentTokenStopPolicy.HasChangedAsync(dbContext, run, cancellationToken)
                        ? CurrentTokenStopPolicy.PolicyChangedDuringClaim()
                        : CurrentClaudeModelPreference.RunChangedDuringClaim());
            }

            var competingRunningAttemptExists = await dbContext.Attempts
                .AsNoTracking()
                .AnyAsync(candidate => candidate.RunId == run.Id && candidate.Status == AttemptStatus.Running, cancellationToken);
            if (competingRunningAttemptExists)
            {
                return Result<CreateImplementationAttemptCommandResult>.Failure(
                    Error.Conflict("attempts.run_has_active_attempt", "This run already has an attempt in progress."));
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
                    return Result<CreateImplementationAttemptCommandResult>.Failure(
                        Error.Conflict("agent_attempts.budget_exhausted", "This run has reached its maximum claimed Agent attempts."));
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
                        return Result<CreateImplementationAttemptCommandResult>.Failure(
                            Error.Failure("agent_attempts.time_budget_evidence_invalid", "This claim's reserved Agent invocation time could not be safely evaluated: prior Agent attempt invocation-time evidence is missing or invalid, or this claim's own reservation is not representable."));
                    }

                    var projectedAgentInvocationTimeOnRace = AgentInvocationTimeReservation.ComputeProjectedReservation(reservedAgentInvocationTimeNow.Value, InvocationTimeout);
                    if (projectedAgentInvocationTimeOnRace is null)
                    {
                        return Result<CreateImplementationAttemptCommandResult>.Failure(
                            Error.Failure("agent_attempts.time_budget_evidence_invalid", "This claim's reserved Agent invocation time could not be safely evaluated: prior Agent attempt invocation-time evidence is missing or invalid, or this claim's own reservation is not representable."));
                    }

                    if (projectedAgentInvocationTimeOnRace.Value > maximumAgentInvocationTimeOnRace)
                    {
                        return Result<CreateImplementationAttemptCommandResult>.Failure(
                            Error.Conflict("agent_attempts.time_budget_exceeded", "This run has reached its maximum reserved Agent invocation time."));
                    }
                }

                // A concurrent claim that also concluded with recorded usage can carry this provider over
                // its token stop even while count capacity remains; that is never a retryable slot conflict.
                var tokenStopOnRace = await AgentTokenStopGate.CheckClaimAsync(dbContext, run, AgentProvider.ClaudeCode, cancellationToken);
                if (tokenStopOnRace is not null)
                {
                    return Result<CreateImplementationAttemptCommandResult>.Failure(tokenStopOnRace);
                }

                return Result<CreateImplementationAttemptCommandResult>.Failure(
                    Error.Conflict("agent_attempts.budget_slot_conflict", "A concurrent request already claimed this Agent attempt's budget slot; retry the request."));
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
        string? directHumanGuidance,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ValidateResolvedPlanCoreAsync(
                run, planProposalMessageId, workspace, checkpoint, evidence, configuredVerificationCommands, directHumanGuidance, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            // A persisted row of the plan's lineage could not be materialized (typically an
            // unparseable stored enum string): fixed refusal, no exception or stored value disclosed.
            // The guard catches InvalidOperationException, which cannot prove an enum-conversion cause and
            // could have another origin; DbException and cancellation exceptions are not caught by it.
            return ResolvedPlanValidation.Failed(PlanningLineage.UnreadableEvidenceError());
        }
    }

    private async Task<ResolvedPlanValidation> ValidateResolvedPlanCoreAsync(
        Run run,
        Guid planProposalMessageId,
        GitWorkspace workspace,
        GitCheckpoint checkpoint,
        GitWorkspaceEvidenceResult evidence,
        IReadOnlyList<ImplementationContextManifestBuilder.VerificationCommandReference> configuredVerificationCommands,
        string? directHumanGuidance,
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
            || !AgentAttemptIdentity.IsCoherent(owningAttempt)
            || owningAttempt.Status != AttemptStatus.Completed
            || proposalMessage.Provenance != CollaborationMessageProvenance.ProviderObserved
            || owningAttempt.AgentProvider is not { } owningProvider
            || !Enum.IsDefined(owningProvider)
            || proposalMessage.Actor != ParticipantIdentity.ForAgent(owningAttempt.AgentRole!.Value, owningProvider))
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
            && owningAttempt.AgentResponseContract == AgentResponseContract.Proposal
            && owningAttempt.AgentOutcome == AgentOutcome.Proposed)
        {
            return await ValidateAcceptedOriginalProposalAsync(
                run, proposalMessage, workspace, checkpoint, evidence, configuredVerificationCommands, directHumanGuidance, cancellationToken);
        }

        if (owningAttempt.AgentRole == AgentRole.Resolver
            && owningAttempt.AgentResponseContract == AgentResponseContract.ChallengeResolution
            && owningAttempt.AgentOutcome == AgentOutcome.Resolved)
        {
            return await ValidateResolvedRevisedProposalAsync(
                run, proposalMessage, workspace, checkpoint, evidence, configuredVerificationCommands, directHumanGuidance, cancellationToken);
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
        string? directHumanGuidance,
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
        if (reviewAttempt is null
            || reviewAttempt.AgentProvider is not { } reviewProvider
            || !Enum.IsDefined(reviewProvider)
            || !AgentAttemptIdentity.IsCoherent(reviewAttempt))
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
        var expectedAcceptanceActor = ParticipantIdentity.ForAgent(AgentRole.CriticalReviewer, reviewProvider);
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
            configuredVerificationCommands,
            evidence.UntrackedFiles,
            directHumanGuidance);

        return ResolvedPlanValidation.Succeeded(manifestJson, [proposalMessage.Id, acceptanceMessage.Id]);
    }

    private async Task<ResolvedPlanValidation> ValidateResolvedRevisedProposalAsync(
        Run run,
        CollaborationMessage revisedProposalMessage,
        GitWorkspace workspace,
        GitCheckpoint checkpoint,
        GitWorkspaceEvidenceResult evidence,
        IReadOnlyList<ImplementationContextManifestBuilder.VerificationCommandReference> configuredVerificationCommands,
        string? directHumanGuidance,
        CancellationToken cancellationToken)
    {
        var eligibility = await EvaluateRevisionEligibilityAsync(run, revisedProposalMessage.Id, workspace, checkpoint, cancellationToken);
        if (eligibility.Error is { } eligibilityError)
        {
            return ResolvedPlanValidation.Failed(eligibilityError);
        }

        var node = eligibility.Node!;
        var acceptance = eligibility.Acceptance;
        var manifestJson = ImplementationContextManifestBuilder.BuildForResolvedRevisedProposal(
            run.ProjectId,
            workspace.Id,
            checkpoint.Id,
            checkpoint.FingerprintSha256,
            run.Objective,
            revisedProposalMessage.Id,
            revisedProposalMessage.Summary,
            revisedProposalMessage.StructuredContentJson,
            node.Decisions
                .Select(decision => new ImplementationContextManifestBuilder.DecisionEvidence(
                    decision.InReplyToMessageId!.Value, decision.Summary, decision.StructuredContentJson))
                .ToArray(),
            evidence.ChangedPaths,
            evidence.CompleteDiff,
            configuredVerificationCommands,
            acceptance is null
                ? null
                : new ImplementationContextManifestBuilder.AcceptanceEvidence(acceptance.Summary, acceptance.StructuredContentJson),
            untrackedFiles: evidence.UntrackedFiles,
            directHumanGuidance: directHumanGuidance);

        var orderedInputMessageIds = new List<Guid> { revisedProposalMessage.Id };
        orderedInputMessageIds.AddRange(node.Decisions.Select(decision => decision.Id));
        if (acceptance is not null)
        {
            orderedInputMessageIds.Add(acceptance.Id);
        }

        return ResolvedPlanValidation.Succeeded(manifestJson, orderedInputMessageIds);
    }

    /// <summary>
    /// If <paramref name="planProposalMessageId"/> names a Proposal not owned by a Planner attempt, decides
    /// its lineage eligibility from durable identity alone (no Git evidence, provider, or manifest
    /// needed) so a refusal does no external work; <see langword="null"/> for any other message, which
    /// the full validation below classifies with its own errors.
    /// </summary>
    private async Task<Error?> EarlyRevisionRefusalAsync(
        Run run, Guid planProposalMessageId, GitWorkspace workspace, GitCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        try
        {
            var message = await dbContext.CollaborationMessages.AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Id == planProposalMessageId, cancellationToken);
            if (message is null || message.RunId != run.Id || message.Type != CollaborationMessageType.Proposal || message.AttemptId is null)
            {
                return null;
            }

            var ownerRole = await dbContext.Attempts.AsNoTracking()
                .Where(candidate => candidate.Id == message.AttemptId)
                .Select(candidate => candidate.AgentRole)
                .SingleOrDefaultAsync(cancellationToken);
            return ownerRole != AgentRole.Planner
                ? (await EvaluateRevisionEligibilityAsync(run, message.Id, workspace, checkpoint, cancellationToken)).Error
                : null;
        }
        catch (InvalidOperationException)
        {
            return PlanningLineage.UnreadableEvidenceError();
        }
    }

    private async Task<RevisionEligibility> EvaluateRevisionEligibilityAsync(
        Run run, Guid revisedProposalMessageId, GitWorkspace workspace, GitCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        // The revision's whole lineage is decided from durable identity: its Resolver attempt, the
        // exact ordered inputs, the parent Proposal, that parent's complete Challenged review, one
        // Decision per Challenge, and the exact workspace and checkpoint. Any missing, foreign,
        // stale, duplicated, or incoherent piece fails closed.
        var snapshot = await PlanningLineage.TryLoadSnapshotAsync(dbContext, run.Id, cancellationToken);
        if (snapshot is null)
        {
            return RevisionEligibility.Failed(PlanningLineage.UnreadableEvidenceError());
        }

        var evaluation = PlanningLineage.Evaluate(
            snapshot, run.Id, workspace.Id, checkpoint.Id, checkpoint.FingerprintSha256, revisedProposalMessageId);
        if (evaluation.Node is not { } node)
        {
            return RevisionEligibility.Failed(evaluation.Failure switch
            {
                PlanningLineage.FailureKind.CheckpointStale => PlanningLineage.ToClaimError(PlanningLineage.FailureKind.CheckpointStale),
                PlanningLineage.FailureKind.OwnerInvalid => PlanningLineage.ToClaimError(PlanningLineage.FailureKind.OwnerInvalid),
                _ => Error.Conflict(
                    "agent_attempts.decisions_not_valid",
                    "The resolver's decision set is not a valid, complete, provider-observed set."),
            });
        }

        // A duplicated root Proposal is corrupt evidence for a plan that will be implemented.
        if (!PlanningLineage.OwnsExactlyOneProposal(snapshot, node.Root.Owner))
        {
            return RevisionEligibility.Failed(Error.Conflict(
                "agent_attempts.decisions_not_valid",
                "The resolver's decision set is not a valid, complete, provider-observed set."));
        }

        // A depth-two revision ended its lineage in a human escalation; it is never implementable.
        if (node.Depth > PlanningLineage.MaximumImplementableDepth)
        {
            return RevisionEligibility.Failed(PlanningLineage.ExhaustedError());
        }

        // A first revision may have received one optional critical review. Challenged blocks it
        // permanently (while its second resolution is pending or failed, and after it succeeded); an
        // Accepted review permits it and binds the implementation to that exact Acceptance.
        var reviews = PlanningLineage.SuccessfulReviewsOf(snapshot, run.Id, node.Proposal.Id);
        if (reviews.Count > 1)
        {
            return RevisionEligibility.Failed(PlanningLineage.ToClaimError(PlanningLineage.FailureKind.LineageInvalid));
        }

        if (reviews.Count == 0)
        {
            return RevisionEligibility.Succeeded(node, null);
        }

        if (reviews[0].AgentOutcome == AgentOutcome.Challenged)
        {
            return RevisionEligibility.Failed(Error.Conflict(
                PlanChallengedCode,
                "This revised plan was challenged by a later critical review and cannot be implemented."));
        }

        var acceptance = PlanningLineage.FindExactAcceptance(
            snapshot, reviews[0], run.Id, node.Proposal.Id, workspace.Id, checkpoint.Id, checkpoint.FingerprintSha256);
        return acceptance is null
            ? RevisionEligibility.Failed(
                Error.Conflict("agent_attempts.acceptance_not_valid", "The acceptance evidence for this proposal is not a valid provider-observed record."))
            : RevisionEligibility.Succeeded(node, acceptance);
    }

    private sealed record RevisionEligibility(PlanningLineage.Node? Node, CollaborationMessage? Acceptance, Error? Error)
    {
        public static RevisionEligibility Succeeded(PlanningLineage.Node node, CollaborationMessage? acceptance) => new(node, acceptance, null);

        public static RevisionEligibility Failed(Error error) => new(null, null, error);
    }

    private sealed record ResolvedPlanValidation(string? ManifestJson, IReadOnlyList<Guid>? OrderedInputMessageIds, Error? Error)
    {
        public static ResolvedPlanValidation Succeeded(string manifestJson, IReadOnlyList<Guid> orderedInputMessageIds) =>
            new(manifestJson, orderedInputMessageIds, null);

        public static ResolvedPlanValidation Failed(Error error) => new(null, null, error);
    }
}
