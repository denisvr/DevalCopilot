using System.Data.Common;
using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Commands.CreateCodexPlanningAttempt;
using DevalCopilot.Application.Features.Runs.Policies.FormatRepair;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Policies;
using DevalCopilot.Application.Features.Runs.Policies.PlanningSequences;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

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
    TimeProvider timeProvider,
    IAttemptDurabilityProbe attemptDurabilityProbe,
    IPlanningSequenceStepProbe? planningStepProbe = null)
    : ICommandHandler<CreateClaudeCriticalReviewAttemptCommand, Result<CreateClaudeCriticalReviewAttemptCommandResult>>
{
    /// <summary>Hard ceiling on the sealed context-manifest artifact — a bounded reference
    /// document, never a transcript or repository copy.</summary>
    private const int MaxContextManifestBytes = 32 * 1024;

    private static readonly TimeSpan InvocationTimeout = AgentClaimPathPolicy.GetInvocationTimeout(AgentClaimPath.ClaudeCriticalReview);
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

        // The execution mode is a durable, immutable admission fact: read afresh (never from the tracked Run)
        // and checked before any workspace, evidence, manifest, or provider work.
        var executionModeError = await CurrentRunExecutionMode.CheckAgentAdmittedAsync(dbContext, run.Id, cancellationToken);
        if (executionModeError is not null)
        {
            return Result<CreateClaudeCriticalReviewAttemptCommandResult>.Failure(executionModeError);
        }

        // A planning sequence (ADR-0055) holds the run against every claim that does not carry its own exact step identity, checked
        // before any external work; the database guard of the same predicate decides at the durable commit. A coordinator step never
        // carries a repair source: the sequence has no format repair.
        var ticket = command.PlanningStep;
        if (ticket is not null && command.RepairSourceAttemptId is not null)
        {
            return Result<CreateClaudeCriticalReviewAttemptCommandResult>.Failure(PlanningSequenceErrors.StepAuthorityLost());
        }

        if (ticket is null
            && await PlanningSequenceFence.CheckRunAsync(dbContext, run.Id, cancellationToken) is { } fenceError)
        {
            return Result<CreateClaudeCriticalReviewAttemptCommandResult>.Failure(fenceError);
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

        if (ticket is not null
            && await PlanningSequenceStepAuthority.CheckEarlyAsync(
                dbContext, ticket, run, AgentRole.CriticalReviewer, workspace, checkpoint, timeProvider.GetUtcNow(), cancellationToken) is { } stepError)
        {
            return Result<CreateClaudeCriticalReviewAttemptCommandResult>.Failure(stepError);
        }

        // A manual format repair derives its target from the source's persisted input and requires an
        // eligible source (see ReadOnlyFormatRepairSource); both are re-evaluated inside the durable
        // claim transaction. It runs before the running-attempt check so a request that lost a race
        // to the source's one repair is told the repair was already requested, not that some attempt
        // is running. An ordinary request skips this entirely and is unaffected.
        var proposalMessageId = command.ProposalMessageId ?? Guid.Empty;
        var repairContext = new ReadOnlyFormatRepairSource.Context(workspace.Id, checkpoint.Id, checkpoint.FingerprintSha256);
        if (command.RepairSourceAttemptId is { } repairSourceAttemptId)
        {
            var repairTarget = await ResolveRepairTargetAsync(run.Id, repairSourceAttemptId, repairContext, cancellationToken);
            if (repairTarget.Error is { } repairTargetError)
            {
                return Result<CreateClaudeCriticalReviewAttemptCommandResult>.Failure(repairTargetError);
            }

            proposalMessageId = repairTarget.ProposalMessageId;
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
            return Result<CreateClaudeCriticalReviewAttemptCommandResult>.Failure(
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
                return Result<CreateClaudeCriticalReviewAttemptCommandResult>.Failure(
                    Error.Failure("agent_attempts.time_budget_evidence_invalid", "This claim's reserved Agent invocation time could not be safely evaluated: prior Agent attempt invocation-time evidence is missing or invalid, or this claim's own reservation is not representable."));
            }

            var projectedAgentInvocationTime = AgentInvocationTimeReservation.ComputeProjectedReservation(reservedAgentInvocationTime.Value, InvocationTimeout);
            if (projectedAgentInvocationTime is null)
            {
                return Result<CreateClaudeCriticalReviewAttemptCommandResult>.Failure(
                    Error.Failure("agent_attempts.time_budget_evidence_invalid", "This claim's reserved Agent invocation time could not be safely evaluated: prior Agent attempt invocation-time evidence is missing or invalid, or this claim's own reservation is not representable."));
            }

            if (projectedAgentInvocationTime.Value > maximumAgentInvocationTime)
            {
                return Result<CreateClaudeCriticalReviewAttemptCommandResult>.Failure(
                    Error.Conflict("agent_attempts.time_budget_exceeded", "This run has reached its maximum reserved Agent invocation time."));
            }
        }

        // The run-scoped, provider-separated token-activity stop (see AgentTokenStopGate), after
        // the run-wide budgets above and before any provider-availability probe, Git work, or
        // manifest sealing. Unconfigured, it reads nothing and changes nothing.
        var tokenStopError = await AgentTokenStopGate.CheckClaimAsync(dbContext, run, AgentProvider.ClaudeCode, cancellationToken);
        if (tokenStopError is not null)
        {
            return Result<CreateClaudeCriticalReviewAttemptCommandResult>.Failure(tokenStopError);
        }

        // The reviewed Proposal's lineage is decided from durable identity alone, before any provider
        // probe, Git capture, or manifest sealing, so a lineage refusal (an exhausted or corrupt lineage,
        // an unsupported role/provider pair, an unreadable row) does no external work. A proposal that is
        // simply not found keeps its existing position after the provider and checkpoint checks below.
        var earlyEligibility = await EvaluateReviewEligibilityAsync(run.Id, proposalMessageId, workspace.Id, checkpoint, cancellationToken);
        if (earlyEligibility.Error is { Code: not "agent_attempts.proposal_not_found" } earlyRefusal)
        {
            return Result<CreateClaudeCriticalReviewAttemptCommandResult>.Failure(earlyRefusal);
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

        var evidence = await evidenceReader.CaptureForAgentContextAsync(
            workspace.WorkspacePath, includeUntrackedPreviews: true, cancellationToken);
        if (evidence.Outcome != GitWorkspaceEvidenceOutcome.Success || evidence.FingerprintSha256 != checkpoint.FingerprintSha256)
        {
            return Result<CreateClaudeCriticalReviewAttemptCommandResult>.Failure(
                Error.Conflict("agent_attempts.checkpoint_not_current", "The selected source checkpoint is no longer current for this workspace."));
        }

        var eligibility = await EvaluateReviewEligibilityAsync(run.Id, proposalMessageId, workspace.Id, checkpoint, cancellationToken);
        if (eligibility.Error is { } error)
        {
            return Result<CreateClaudeCriticalReviewAttemptCommandResult>.Failure(error);
        }

        var proposalMessage = eligibility.Message!;

        if (ticket is not null && ticket.InputProposalMessageId != proposalMessage.Id)
        {
            return Result<CreateClaudeCriticalReviewAttemptCommandResult>.Failure(PlanningSequenceErrors.StepInputMismatch());
        }

        var attemptId = ticket?.ReservedAttemptId ?? Guid.NewGuid();
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
            TrackedChangeEvidence.From(evidence),
            ProjectInstructionContextManifest.Prepare(workspace.Id, checkpoint.Id, checkpoint.FingerprintSha256, evidence.InstructionContext),
            evidence.UntrackedFiles,
            formatRepair: command.RepairSourceAttemptId is not null);
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

        // A coordinator step commits through its own short write-locked transaction (see CommitStepAsync): the step.s exact authority,
        // the pinned source, the reviewed lineage and the run-wide Running slot are decided after SQLite.s write lock is held, and the
        // step link commits in the same transaction as the attempt.
        if (ticket is not null)
        {
            return await CommitStepAsync(
                run, workspace, checkpoint, ticket, proposalMessage, attemptId, manifestArtifactId, sealedManifest, nowUtc,
                agentAttemptsUsed, cancellationToken);
        }

        // A repair commits through its own short write-locked transaction (see CommitRepairAsync):
        // the source, its exact input, the lineage, and the run-wide Running slot are re-read there,
        // after a write guard holds SQLite's write lock, so a late read alone never decides it.
        if (command.RepairSourceAttemptId is { } repairSourceAtCommit)
        {
            return await CommitRepairAsync(
                run, workspace, checkpoint, repairContext, repairSourceAtCommit, proposalMessage, attemptId, manifestArtifactId,
                sealedManifest, nowUtc, agentAttemptsUsed, cancellationToken);
        }

        // The last read before the durable commit: the lineage and the run-wide Running slot are
        // decided again after the external work above, so a competing review or claim that landed
        // in the meantime is refused here (the sealed manifest is removed) rather than committed.
        // The filtered unique index on (RunId WHERE Status = 'Running') still backstops the commit.
        var lateEligibility = await EvaluateReviewEligibilityAsync(run.Id, proposalMessageId, workspace.Id, checkpoint, cancellationToken);
        Error? lateError = lateEligibility.Error;
        if (lateError is null
            && await dbContext.Attempts.AsNoTracking().AnyAsync(
                candidate => candidate.RunId == run.Id && candidate.Status == AttemptStatus.Running, cancellationToken))
        {
            lateError = Error.Conflict("attempts.run_has_active_attempt", "This run already has an attempt in progress.");
        }

        if (lateError is not null)
        {
            artifactStore.DeleteOrphanedSealedFile(run.Id, attemptId, ArtifactPurpose.AgentContextManifest);
            return Result<CreateClaudeCriticalReviewAttemptCommandResult>.Failure(lateError);
        }

        var attemptNumber = await dbContext.Attempts.Where(candidate => candidate.RunId == run.Id).CountAsync(cancellationToken) + 1;
        var agentBudgetSlot = agentAttemptsUsed + 1;

        // Read as late as possible (after every external step) and guarded by the Run's
        // concurrency token, so this Attempt snapshots exactly the model and effort pair that is still current
        // when the single SaveChangesAsync below commits — never an earlier-loaded, stale value.
        // The execution mode is read afresh and guarded by its concurrency token, like the model request: the
        // single SaveChangesAsync below commits only while the stored mode is still the one decided against.
        var guardedModeError = await CurrentRunExecutionMode.ReadAndGuardAgentAsync(dbContext, run, cancellationToken);
        if (guardedModeError is not null)
        {
            artifactStore.DeleteOrphanedSealedFile(run.Id, attemptId, ArtifactPurpose.AgentContextManifest);
            return Result<CreateClaudeCriticalReviewAttemptCommandResult>.Failure(guardedModeError);
        }

        var requestedClaude = await CurrentClaudeModelPreference.ReadAndGuardAsync(dbContext, run, cancellationToken);

        // The token stop's own commit-time guard: the claim's Run UPDATE also requires the exact stop
        // policy this claim decided against (see CurrentTokenStopPolicy).
        CurrentTokenStopPolicy.Guard(dbContext, run);

        var attempt = Attempt.ClaimAgentCriticalReviewWithModelRequest(
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
            requestedClaude.Model,
            requestedClaude.Effort,
            agentBudgetSlot);
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

            if (exception is DbUpdateConcurrencyException)
            {
                // The Run's lifecycle or Claude model request changed between this claim's late
                // read and its commit (see CurrentClaudeModelPreference); the whole batch rolled
                // back, so nothing persisted and a retry re-reads the current state.
                // A token stop policy change is named as such; anything else keeps the generic
                // run-changed conflict.
                return Result<CreateClaudeCriticalReviewAttemptCommandResult>.Failure(
                    await CurrentRunExecutionMode.HasChangedAsync(dbContext, run, cancellationToken)
                        ? CurrentRunExecutionMode.ChangedDuringClaim()
                        : await CurrentTokenStopPolicy.HasChangedAsync(dbContext, run, cancellationToken)
                        ? CurrentTokenStopPolicy.PolicyChangedDuringClaim()
                        : CurrentClaudeModelPreference.RunChangedDuringClaim());
            }

            if (await PlanningSequenceFence.RefusedRunSaveAsync(dbContext, run.Id))
            {
                // The database guard of an armed sequence refused this claim at the write seam (ADR-0055).
                return Result<CreateClaudeCriticalReviewAttemptCommandResult>.Failure(PlanningSequenceErrors.ManualControlBlocked());
            }

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
                    return Result<CreateClaudeCriticalReviewAttemptCommandResult>.Failure(
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
                        return Result<CreateClaudeCriticalReviewAttemptCommandResult>.Failure(
                            Error.Failure("agent_attempts.time_budget_evidence_invalid", "This claim's reserved Agent invocation time could not be safely evaluated: prior Agent attempt invocation-time evidence is missing or invalid, or this claim's own reservation is not representable."));
                    }

                    var projectedAgentInvocationTimeOnRace = AgentInvocationTimeReservation.ComputeProjectedReservation(reservedAgentInvocationTimeNow.Value, InvocationTimeout);
                    if (projectedAgentInvocationTimeOnRace is null)
                    {
                        return Result<CreateClaudeCriticalReviewAttemptCommandResult>.Failure(
                            Error.Failure("agent_attempts.time_budget_evidence_invalid", "This claim's reserved Agent invocation time could not be safely evaluated: prior Agent attempt invocation-time evidence is missing or invalid, or this claim's own reservation is not representable."));
                    }

                    if (projectedAgentInvocationTimeOnRace.Value > maximumAgentInvocationTimeOnRace)
                    {
                        return Result<CreateClaudeCriticalReviewAttemptCommandResult>.Failure(
                            Error.Conflict("agent_attempts.time_budget_exceeded", "This run has reached its maximum reserved Agent invocation time."));
                    }
                }

                // A concurrent claim that also concluded with recorded usage can carry this provider over
                // its token stop even while count capacity remains; that is never a retryable slot conflict.
                var tokenStopOnRace = await AgentTokenStopGate.CheckClaimAsync(dbContext, run, AgentProvider.ClaudeCode, cancellationToken);
                if (tokenStopOnRace is not null)
                {
                    return Result<CreateClaudeCriticalReviewAttemptCommandResult>.Failure(tokenStopOnRace);
                }

                return Result<CreateClaudeCriticalReviewAttemptCommandResult>.Failure(
                    Error.Conflict("agent_attempts.budget_slot_conflict", "A concurrent request already claimed this Agent attempt's budget slot; retry the request."));
            }

            // Not a race loss on any known invariant — no competing Running attempt and no
            // competing budget-slot claim exist for this run at all. Some other persistence
            // failure caused this (disk, corruption, an unrelated constraint); never report a
            // conflict that would falsely imply a race that
            // never happened.
            return Result<CreateClaudeCriticalReviewAttemptCommandResult>.Failure(
                Error.Failure("attempts.persistence_failed", "The attempt could not be durably recorded."));
        }

        return Result<CreateClaudeCriticalReviewAttemptCommandResult>.Success(
            new CreateClaudeCriticalReviewAttemptCommandResult(attempt.Id, attempt.AttemptNumber));
    }

    /// <summary>
    /// Decides, from durable identity alone, whether <paramref name="proposalMessageId"/> may
    /// receive a critical review: it is a valid lineage Proposal (a provider-observed Planner root,
    /// or a Resolver's first revision), bound to the exact workspace, checkpoint, and fingerprint
    /// this attempt is about to claim, at a depth that still allows a review, and has no earlier
    /// successful review. A depth-two revision is the end of the lineage and is never reviewable.
    /// </summary>
    private async Task<ReviewEligibility> EvaluateReviewEligibilityAsync(
        Guid runId, Guid proposalMessageId, Guid workspaceId, GitCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        var snapshot = await PlanningLineage.TryLoadSnapshotAsync(dbContext, runId, cancellationToken);
        if (snapshot is null)
        {
            return ReviewEligibility.Failed(PlanningLineage.UnreadableEvidenceError());
        }

        var evaluation = PlanningLineage.Evaluate(
            snapshot, runId, workspaceId, checkpoint.Id, checkpoint.FingerprintSha256, proposalMessageId);
        if (evaluation.Node is not { } node)
        {
            return ReviewEligibility.Failed(PlanningLineage.ToClaimError(evaluation.Failure ?? PlanningLineage.FailureKind.LineageInvalid));
        }

        if (node.Depth > PlanningLineage.MaximumReviewableDepth)
        {
            return ReviewEligibility.Failed(PlanningLineage.ExhaustedError());
        }

        var alreadyReviewed = await dbContext.Attempts
            .AsNoTracking()
            .Join(
                dbContext.AttemptInputMessages.AsNoTracking().Where(inputMessage => inputMessage.CollaborationMessageId == node.Proposal.Id),
                attempt => attempt.Id,
                inputMessage => inputMessage.AttemptId,
                (attempt, inputMessage) => attempt)
            .AnyAsync(
                candidate =>
                    candidate.Kind == AttemptKind.Agent
                    && candidate.AgentRole == AgentRole.CriticalReviewer
                    && (candidate.AgentOutcome == AgentOutcome.Accepted || candidate.AgentOutcome == AgentOutcome.Challenged),
                cancellationToken);
        return alreadyReviewed
            ? ReviewEligibility.Failed(Error.Conflict("agent_attempts.already_reviewed", "This proposal already has a successful critical review."))
            : ReviewEligibility.Succeeded(node.Proposal);
    }

    /// <summary>Evaluates the repair source (see <see cref="ReadOnlyFormatRepairSource"/>) and derives the
    /// reviewed Proposal from its one persisted input. Nothing here is supplied by the caller.</summary>
    private async Task<(Guid ProposalMessageId, Error? Error)> ResolveRepairTargetAsync(
        Guid runId, Guid repairSourceAttemptId, ReadOnlyFormatRepairSource.Context repairContext, CancellationToken cancellationToken)
    {
        var evaluation = await ReadOnlyFormatRepairSource.EvaluateAsync(
            dbContext, runId, repairSourceAttemptId, AgentResponseContract.CriticalReview, repairContext, cancellationToken);
        if (evaluation.Error is { } sourceError)
        {
            return (Guid.Empty, sourceError);
        }

        var sourceInputs = await ReadOnlyFormatRepairInputs.ReadAsync(
            dbContext, repairSourceAttemptId, AgentResponseContract.CriticalReview, cancellationToken);
        return sourceInputs is null
            ? (Guid.Empty, ReadOnlyFormatRepairSource.Ineligible())
            : (sourceInputs.OrderedMessageIds[0], null);
    }

    /// <summary>The repair's final in-transaction read, decided against the state SQLite's write lock now
    /// protects: the source is still eligible with the same one input, the reviewed lineage still
    /// allows this review, and no attempt is Running. Returns the refusal, or null when still eligible.</summary>
    private async Task<Error?> RevalidateRepairAsync(
        Run run,
        GitWorkspace workspace,
        GitCheckpoint checkpoint,
        ReadOnlyFormatRepairSource.Context repairContext,
        Guid repairSourceAttemptId,
        Guid proposalMessageId,
        CancellationToken cancellationToken)
    {
        if (await ReadOnlyFormatRepairSource.EvaluateContextStillCurrentAsync(
                dbContext, run.Id, workspace.Id, checkpoint.Id, runMayBeCreated: true, cancellationToken) is { } contextError)
        {
            return contextError;
        }

        var target = await ResolveRepairTargetAsync(run.Id, repairSourceAttemptId, repairContext, cancellationToken);
        if (target.Error is { } targetError)
        {
            return targetError;
        }

        if (target.ProposalMessageId != proposalMessageId)
        {
            return ReadOnlyFormatRepairSource.InputsMismatch();
        }

        var eligibility = await EvaluateReviewEligibilityAsync(run.Id, proposalMessageId, workspace.Id, checkpoint, cancellationToken);
        if (eligibility.Error is { } eligibilityError)
        {
            return eligibilityError;
        }

        return await dbContext.Attempts.AsNoTracking().AnyAsync(
            candidate => candidate.RunId == run.Id && candidate.Status == AttemptStatus.Running, cancellationToken)
            ? Error.Conflict("attempts.run_has_active_attempt", "This run already has an attempt in progress.")
            : null;
    }

    /// <summary>
    /// The durable claim of the one manual format repair. One short, explicit transaction — opened only
    /// after all external work (Git evidence, manifest sealing) has completed — first executes a write
    /// guard (the token-stop policy compare, an atomic UPDATE ... WHERE), which takes SQLite's write
    /// lock; only then are the source, its exact input, the reviewed lineage, the running slot, and the
    /// Claude model preference read, so no other claim can commit between those reads and this claim's
    /// own inserts and commit. Every failure path removes the already-sealed manifest unless a fresh
    /// independent probe shows the Attempt durably persisted or could not resolve it, in which case the
    /// file is preserved; cancellation always propagates and is never converted into a Result.
    /// </summary>
    private async Task<Result<CreateClaudeCriticalReviewAttemptCommandResult>> CommitRepairAsync(
        Run run,
        GitWorkspace workspace,
        GitCheckpoint checkpoint,
        ReadOnlyFormatRepairSource.Context repairContext,
        Guid repairSourceAttemptId,
        CollaborationMessage proposalMessage,
        Guid attemptId,
        Guid manifestArtifactId,
        SealedOutputFile sealedManifest,
        DateTimeOffset nowUtc,
        int agentAttemptsUsed,
        CancellationToken cancellationToken)
    {
        IDbContextTransaction claimTransaction;
        try
        {
            claimTransaction = await dbContext.BeginTransactionAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            artifactStore.DeleteOrphanedSealedFile(run.Id, attemptId, ArtifactPurpose.AgentContextManifest);
            throw;
        }
        catch (DbException)
        {
            artifactStore.DeleteOrphanedSealedFile(run.Id, attemptId, ArtifactPurpose.AgentContextManifest);
            return Result<CreateClaudeCriticalReviewAttemptCommandResult>.Failure(
                Error.Failure("attempts.persistence_failed", "The attempt could not be durably recorded."));
        }

        await using (claimTransaction)
        {
            bool stopPolicyStillCurrent;
            Error? revalidationError = null;
            ClaudeModelRequestSnapshot requestedClaude = default;
            int attemptNumber = 0;
            try
            {
                // The write guard: one atomic UPDATE ... WHERE, the first statement of the transaction,
                // so the write lock is held before any read below.
                stopPolicyStillCurrent = await CurrentTokenStopPolicy.ConfirmUnchangedAsync(dbContext, run, cancellationToken);
                if (stopPolicyStillCurrent)
                {
                    revalidationError = await RevalidateRepairAsync(
                        run, workspace, checkpoint, repairContext, repairSourceAttemptId, proposalMessage.Id, cancellationToken);
                    if (revalidationError is null)
                    {
                        // Read afresh inside the transaction and guarded for the commit, like the model request.
                        revalidationError = await CurrentRunExecutionMode.ReadAndGuardAgentAsync(dbContext, run, cancellationToken);
                    }

                    if (revalidationError is null)
                    {
                        attemptNumber = await dbContext.Attempts.Where(candidate => candidate.RunId == run.Id).CountAsync(cancellationToken) + 1;
                        requestedClaude = await CurrentClaudeModelPreference.ReadAndGuardAsync(dbContext, run, cancellationToken);
                        CurrentTokenStopPolicy.Guard(dbContext, run);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                await RollbackBestEffortAsync(claimTransaction, CancellationToken.None);
                artifactStore.DeleteOrphanedSealedFile(run.Id, attemptId, ArtifactPurpose.AgentContextManifest);
                throw;
            }
            catch (DbException)
            {
                // A raw provider failure (a bounded lock-wait timeout or any other database failure) is
                // never inferred to mean the source or its inputs changed.
                await RollbackBestEffortAsync(claimTransaction, cancellationToken);
                artifactStore.DeleteOrphanedSealedFile(run.Id, attemptId, ArtifactPurpose.AgentContextManifest);
                return Result<CreateClaudeCriticalReviewAttemptCommandResult>.Failure(
                    Error.Failure("attempts.persistence_failed", "The attempt could not be durably recorded."));
            }

            if (!stopPolicyStillCurrent)
            {
                await RollbackBestEffortAsync(claimTransaction, cancellationToken);
                artifactStore.DeleteOrphanedSealedFile(run.Id, attemptId, ArtifactPurpose.AgentContextManifest);
                return Result<CreateClaudeCriticalReviewAttemptCommandResult>.Failure(CurrentTokenStopPolicy.PolicyChangedDuringClaim());
            }

            if (revalidationError is not null)
            {
                // The source, its input, the lineage, or the running slot changed after the earlier
                // checks: no repair is claimed, nothing is consumed, and the sealed manifest is removed.
                await RollbackBestEffortAsync(claimTransaction, cancellationToken);
                artifactStore.DeleteOrphanedSealedFile(run.Id, attemptId, ArtifactPurpose.AgentContextManifest);
                return Result<CreateClaudeCriticalReviewAttemptCommandResult>.Failure(revalidationError);
            }

            var agentBudgetSlot = agentAttemptsUsed + 1;
            var attempt = Attempt.ClaimAgentCriticalReviewWithModelRequest(
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
                requestedClaude.Model,
                requestedClaude.Effort,
                agentBudgetSlot,
                repairSourceAttemptId);
            var inputMessage = AttemptInputMessage.Record(Guid.NewGuid(), attemptId, proposalMessage.Id, sequence: 0);
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

            dbContext.Attempts.Add(attempt);
            dbContext.AttemptInputMessages.Add(inputMessage);
            dbContext.Artifacts.Add(manifestArtifact);

            if (run.Lifecycle == RunLifecycle.Created)
            {
                run.Claim(nowUtc);
            }

            void RemoveTracked()
            {
                dbContext.Attempts.Remove(attempt);
                dbContext.AttemptInputMessages.Remove(inputMessage);
                dbContext.Artifacts.Remove(manifestArtifact);
            }

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // The database may already have applied the insert; an independent probe is the sole
                // authority for the manifest's fate, and the cancellation still propagates.
                await RollbackBestEffortAsync(claimTransaction, CancellationToken.None);
                var durability = await attemptDurabilityProbe.CheckAsync(attemptId, CancellationToken.None);
                if (durability == AttemptDurabilityCheckResult.NotPersisted)
                {
                    artifactStore.DeleteOrphanedSealedFile(run.Id, attemptId, ArtifactPurpose.AgentContextManifest);
                    RemoveTracked();
                }

                throw;
            }
            catch (DbUpdateException exception)
            {
                await RollbackBestEffortAsync(claimTransaction, cancellationToken);

                var durability = await attemptDurabilityProbe.CheckAsync(attemptId, cancellationToken);
                if (durability == AttemptDurabilityCheckResult.Persisted)
                {
                    return Result<CreateClaudeCriticalReviewAttemptCommandResult>.Success(
                        new CreateClaudeCriticalReviewAttemptCommandResult(attemptId, attemptNumber, repairSourceAttemptId));
                }

                if (durability == AttemptDurabilityCheckResult.Unresolved)
                {
                    return Result<CreateClaudeCriticalReviewAttemptCommandResult>.Failure(Error.Failure(
                        "attempts.persistence_unresolved",
                        "Whether the attempt was durably recorded could not be confirmed."));
                }

                artifactStore.DeleteOrphanedSealedFile(run.Id, attemptId, ArtifactPurpose.AgentContextManifest);
                RemoveTracked();

                if (exception is DbUpdateConcurrencyException)
                {
                    return Result<CreateClaudeCriticalReviewAttemptCommandResult>.Failure(
                        await CurrentRunExecutionMode.HasChangedAsync(dbContext, run, cancellationToken)
                            ? CurrentRunExecutionMode.ChangedDuringClaim()
                            : await CurrentTokenStopPolicy.HasChangedAsync(dbContext, run, cancellationToken)
                            ? CurrentTokenStopPolicy.PolicyChangedDuringClaim()
                            : CurrentClaudeModelPreference.RunChangedDuringClaim());
                }

                if (await PlanningSequenceFence.RefusedRunSaveAsync(dbContext, run.Id))
                {
                    // The database guard of an armed sequence refused this claim at the write seam (ADR-0055).
                    return Result<CreateClaudeCriticalReviewAttemptCommandResult>.Failure(PlanningSequenceErrors.ManualControlBlocked());
                }

                if (await dbContext.Attempts.AsNoTracking().AnyAsync(
                        candidate => candidate.AgentRepairSourceAttemptId == repairSourceAttemptId, cancellationToken))
                {
                    // The race the (AgentRepairSourceAttemptId) unique index exists to close.
                    return Result<CreateClaudeCriticalReviewAttemptCommandResult>.Failure(Error.Conflict(
                        PlanningRepairSource.AlreadyRequestedCode, "A repair was already requested for this attempt."));
                }

                if (await dbContext.Attempts.AsNoTracking().AnyAsync(
                        candidate => candidate.RunId == run.Id && candidate.Status == AttemptStatus.Running, cancellationToken))
                {
                    return Result<CreateClaudeCriticalReviewAttemptCommandResult>.Failure(
                        Error.Conflict("attempts.run_has_active_attempt", "This run already has an attempt in progress."));
                }

                if (await dbContext.Attempts.AsNoTracking().AnyAsync(
                        candidate => candidate.RunId == run.Id && candidate.Kind == AttemptKind.Agent && candidate.AgentBudgetSlot == agentBudgetSlot,
                        cancellationToken))
                {
                    var agentAttemptsUsedNow = await dbContext.Attempts.AsNoTracking().CountAsync(
                        candidate => candidate.RunId == run.Id && candidate.Kind == AttemptKind.Agent, cancellationToken);
                    if (agentAttemptsUsedNow >= run.MaximumAgentAttempts)
                    {
                        return Result<CreateClaudeCriticalReviewAttemptCommandResult>.Failure(
                            Error.Conflict("agent_attempts.budget_exhausted", "This run has reached its maximum claimed Agent attempts."));
                    }

                    if (run.MaximumAgentInvocationTime is { } maximumAgentInvocationTimeOnRace)
                    {
                        var reservedNow = await AgentInvocationTimeBudget.ComputeReservedAsync(dbContext, run.Id, asNoTracking: true, cancellationToken);
                        var projectedOnRace = reservedNow is null
                            ? null
                            : AgentInvocationTimeReservation.ComputeProjectedReservation(reservedNow.Value, InvocationTimeout);
                        if (projectedOnRace is null)
                        {
                            return Result<CreateClaudeCriticalReviewAttemptCommandResult>.Failure(
                                Error.Failure("agent_attempts.time_budget_evidence_invalid", "This claim's reserved Agent invocation time could not be safely evaluated: prior Agent attempt invocation-time evidence is missing or invalid, or this claim's own reservation is not representable."));
                        }

                        if (projectedOnRace.Value > maximumAgentInvocationTimeOnRace)
                        {
                            return Result<CreateClaudeCriticalReviewAttemptCommandResult>.Failure(
                                Error.Conflict("agent_attempts.time_budget_exceeded", "This run has reached its maximum reserved Agent invocation time."));
                        }
                    }

                    var tokenStopOnRace = await AgentTokenStopGate.CheckClaimAsync(dbContext, run, AgentProvider.ClaudeCode, cancellationToken);
                    if (tokenStopOnRace is not null)
                    {
                        return Result<CreateClaudeCriticalReviewAttemptCommandResult>.Failure(tokenStopOnRace);
                    }

                    return Result<CreateClaudeCriticalReviewAttemptCommandResult>.Failure(
                        Error.Conflict("agent_attempts.budget_slot_conflict", "A concurrent request already claimed this Agent attempt's budget slot; retry the request."));
                }

                return Result<CreateClaudeCriticalReviewAttemptCommandResult>.Failure(
                    Error.Failure("attempts.persistence_failed", "The attempt could not be durably recorded."));
            }

            try
            {
                await claimTransaction.CommitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                await RollbackBestEffortAsync(claimTransaction, CancellationToken.None);
                var durability = await attemptDurabilityProbe.CheckAsync(attemptId, CancellationToken.None);
                if (durability == AttemptDurabilityCheckResult.NotPersisted)
                {
                    artifactStore.DeleteOrphanedSealedFile(run.Id, attemptId, ArtifactPurpose.AgentContextManifest);
                    RemoveTracked();
                }

                throw;
            }
            catch (DbException)
            {
                await RollbackBestEffortAsync(claimTransaction, cancellationToken);

                var durability = await attemptDurabilityProbe.CheckAsync(attemptId, cancellationToken);
                if (durability == AttemptDurabilityCheckResult.Persisted)
                {
                    return Result<CreateClaudeCriticalReviewAttemptCommandResult>.Success(
                        new CreateClaudeCriticalReviewAttemptCommandResult(attemptId, attemptNumber, repairSourceAttemptId));
                }

                if (durability == AttemptDurabilityCheckResult.Unresolved)
                {
                    return Result<CreateClaudeCriticalReviewAttemptCommandResult>.Failure(Error.Failure(
                        "attempts.persistence_unresolved",
                        "Whether the attempt was durably recorded could not be confirmed."));
                }

                artifactStore.DeleteOrphanedSealedFile(run.Id, attemptId, ArtifactPurpose.AgentContextManifest);
                RemoveTracked();
                return Result<CreateClaudeCriticalReviewAttemptCommandResult>.Failure(
                    Error.Failure("attempts.persistence_failed", "The attempt could not be durably recorded."));
            }

            return Result<CreateClaudeCriticalReviewAttemptCommandResult>.Success(
                new CreateClaudeCriticalReviewAttemptCommandResult(attemptId, attemptNumber, repairSourceAttemptId));
        }
    }

    /// <summary>
    /// The durable claim of one coordinator step (ADR-0055). One short, explicit transaction, opened only after all external work (Git
    /// evidence, manifest sealing) has completed, starts with the compare-and-set that confirms and locks the exact reserved step, so a
    /// Stop, an expiry or a restart that committed first leaves it with no matching row. Only then are the pinned source, the reviewed
    /// lineage, the run-wide Running slot and the Claude model preference read, and the attempt, its input, its artifact metadata, its
    /// budget consumption and the step link are saved and committed together: there is no claim-then-link gap. A failed or ambiguous
    /// save is resolved only by an independent read of the exact step/attempt association; absence or uncertainty never authorizes
    /// another claim attempt, and a possibly referenced manifest is preserved unless the read proves it orphaned.
    /// </summary>
    private async Task<Result<CreateClaudeCriticalReviewAttemptCommandResult>> CommitStepAsync(
        Run run,
        GitWorkspace workspace,
        GitCheckpoint checkpoint,
        PlanningSequenceStepTicket ticket,
        CollaborationMessage proposalMessage,
        Guid attemptId,
        Guid manifestArtifactId,
        SealedOutputFile sealedManifest,
        DateTimeOffset nowUtc,
        int agentAttemptsUsed,
        CancellationToken cancellationToken)
    {
        IDbContextTransaction claimTransaction;
        try
        {
            claimTransaction = await dbContext.BeginTransactionAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            artifactStore.DeleteOrphanedSealedFile(run.Id, attemptId, ArtifactPurpose.AgentContextManifest);
            throw;
        }
        catch (DbException)
        {
            artifactStore.DeleteOrphanedSealedFile(run.Id, attemptId, ArtifactPurpose.AgentContextManifest);
            return Result<CreateClaudeCriticalReviewAttemptCommandResult>.Failure(
                Error.Failure("attempts.persistence_failed", "The attempt could not be durably recorded."));
        }

        await using (claimTransaction)
        {
            Error? seamError;
            PlanningSequenceStepLink? stepLink;
            bool stopPolicyStillCurrent = true;
            Error? revalidationError = null;
            ClaudeModelRequestSnapshot requestedClaude = default;
            int attemptNumber = 0;
            try
            {
                // The first statement of the transaction: the compare-and-set on the exact step, which takes the write lock.
                (seamError, stepLink) = await PlanningSequenceStepAuthority.ConfirmAtSeamAsync(
                    dbContext, ticket, run, AgentRole.CriticalReviewer, timeProvider.GetUtcNow(), cancellationToken);
                if (seamError is null)
                {
                    stopPolicyStillCurrent = await CurrentTokenStopPolicy.ConfirmUnchangedAsync(dbContext, run, cancellationToken);
                    if (stopPolicyStillCurrent)
                    {
                        var eligibility = await EvaluateReviewEligibilityAsync(
                            run.Id, proposalMessage.Id, workspace.Id, checkpoint, cancellationToken);
                        revalidationError = eligibility.Error
                            ?? (await dbContext.Attempts.AsNoTracking().AnyAsync(
                                candidate => candidate.RunId == run.Id && candidate.Status == AttemptStatus.Running, cancellationToken)
                                ? Error.Conflict("attempts.run_has_active_attempt", "This run already has an attempt in progress.")
                                : null);
                        if (revalidationError is null)
                        {
                            // Read afresh inside the transaction and guarded for the commit, like the model request.
                            revalidationError = await CurrentRunExecutionMode.ReadAndGuardAgentAsync(dbContext, run, cancellationToken);
                        }

                        if (revalidationError is null
                            && !await CurrentRunLifecycle.IsStillActiveAsync(dbContext, run.Id, allowCreated: true, cancellationToken))
                        {
                            revalidationError = CurrentRunLifecycle.NotActive();
                        }

                        if (revalidationError is null)
                        {
                            attemptNumber = await dbContext.Attempts.Where(candidate => candidate.RunId == run.Id).CountAsync(cancellationToken) + 1;
                            requestedClaude = await CurrentClaudeModelPreference.ReadAndGuardAsync(dbContext, run, cancellationToken);
                            CurrentTokenStopPolicy.Guard(dbContext, run);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                await RollbackBestEffortAsync(claimTransaction, CancellationToken.None);
                artifactStore.DeleteOrphanedSealedFile(run.Id, attemptId, ArtifactPurpose.AgentContextManifest);
                throw;
            }
            catch (DbException)
            {
                // A raw provider failure is never inferred to mean the step authority or the source changed.
                await RollbackBestEffortAsync(claimTransaction, cancellationToken);
                artifactStore.DeleteOrphanedSealedFile(run.Id, attemptId, ArtifactPurpose.AgentContextManifest);
                return Result<CreateClaudeCriticalReviewAttemptCommandResult>.Failure(
                    Error.Failure("attempts.persistence_failed", "The attempt could not be durably recorded."));
            }

            if (seamError is not null || !stopPolicyStillCurrent || revalidationError is not null)
            {
                // The sequence no longer authorizes this step, or the lineage, the slot or a policy changed after the earlier checks:
                // nothing is claimed or consumed, and the sealed manifest is removed.
                await RollbackBestEffortAsync(claimTransaction, cancellationToken);
                if (seamError is null || await ProbeStepAsync(ticket, CancellationToken.None) == AttemptDurabilityCheckResult.NotPersisted)
                {
                    // A step claim whose compare-and-set was lost shares the winner's sealed path: it is removed only when an independent
                    // read proves no other worker claimed the step, never when the winner may reference it.
                    artifactStore.DeleteOrphanedSealedFile(run.Id, attemptId, ArtifactPurpose.AgentContextManifest);
                }

                return Result<CreateClaudeCriticalReviewAttemptCommandResult>.Failure(
                    seamError ?? revalidationError ?? CurrentTokenStopPolicy.PolicyChangedDuringClaim());
            }

            var agentBudgetSlot = agentAttemptsUsed + 1;
            var attempt = Attempt.ClaimAgentCriticalReviewWithModelRequest(
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
                requestedClaude.Model,
                requestedClaude.Effort,
                agentBudgetSlot);
            var inputMessage = AttemptInputMessage.Record(Guid.NewGuid(), attemptId, proposalMessage.Id, sequence: 0);
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

            dbContext.Attempts.Add(attempt);
            dbContext.AttemptInputMessages.Add(inputMessage);
            dbContext.Artifacts.Add(manifestArtifact);

            if (run.Lifecycle == RunLifecycle.Created)
            {
                run.Claim(nowUtc);
            }

            // The step link and its event commit with the attempt, its input, its artifact and its budget consumption.
            stepLink!.Apply(dbContext, attemptId, nowUtc);

            void RemoveTracked()
            {
                dbContext.Attempts.Remove(attempt);
                dbContext.AttemptInputMessages.Remove(inputMessage);
                dbContext.Artifacts.Remove(manifestArtifact);
            }

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // The database may already have applied the save; an independent read is the sole authority for the manifest's fate,
                // and the cancellation still propagates.
                await RollbackBestEffortAsync(claimTransaction, CancellationToken.None);
                var durability = await ProbeStepAsync(ticket, CancellationToken.None);
                if (durability == AttemptDurabilityCheckResult.NotPersisted)
                {
                    artifactStore.DeleteOrphanedSealedFile(run.Id, attemptId, ArtifactPurpose.AgentContextManifest);
                    RemoveTracked();
                }

                throw;
            }
            catch (DbUpdateException exception)
            {
                await RollbackBestEffortAsync(claimTransaction, cancellationToken);

                var durability = await ProbeStepAsync(ticket, cancellationToken);
                if (durability == AttemptDurabilityCheckResult.Persisted)
                {
                    return Result<CreateClaudeCriticalReviewAttemptCommandResult>.Success(
                        new CreateClaudeCriticalReviewAttemptCommandResult(attemptId, attemptNumber));
                }

                if (durability == AttemptDurabilityCheckResult.Unresolved)
                {
                    return Result<CreateClaudeCriticalReviewAttemptCommandResult>.Failure(Error.Failure(
                        "attempts.persistence_unresolved",
                        "Whether the attempt was durably recorded could not be confirmed."));
                }

                artifactStore.DeleteOrphanedSealedFile(run.Id, attemptId, ArtifactPurpose.AgentContextManifest);
                RemoveTracked();

                // The step's own compare-and-set or any other refusal of a step claim is never reinterpreted as a race that allows a
                // retry: the sequence decides what happens next.
                return Result<CreateClaudeCriticalReviewAttemptCommandResult>.Failure(
                    exception is DbUpdateConcurrencyException
                        ? PlanningSequenceErrors.StepAuthorityLost()
                        : Error.Failure("attempts.persistence_failed", "The attempt could not be durably recorded."));
            }

            try
            {
                await claimTransaction.CommitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                await RollbackBestEffortAsync(claimTransaction, CancellationToken.None);
                var durability = await ProbeStepAsync(ticket, CancellationToken.None);
                if (durability == AttemptDurabilityCheckResult.NotPersisted)
                {
                    artifactStore.DeleteOrphanedSealedFile(run.Id, attemptId, ArtifactPurpose.AgentContextManifest);
                    RemoveTracked();
                }

                throw;
            }
            catch (DbException)
            {
                await RollbackBestEffortAsync(claimTransaction, cancellationToken);

                var durability = await ProbeStepAsync(ticket, cancellationToken);
                if (durability == AttemptDurabilityCheckResult.Persisted)
                {
                    return Result<CreateClaudeCriticalReviewAttemptCommandResult>.Success(
                        new CreateClaudeCriticalReviewAttemptCommandResult(attemptId, attemptNumber));
                }

                if (durability == AttemptDurabilityCheckResult.Unresolved)
                {
                    return Result<CreateClaudeCriticalReviewAttemptCommandResult>.Failure(Error.Failure(
                        "attempts.persistence_unresolved",
                        "Whether the attempt was durably recorded could not be confirmed."));
                }

                artifactStore.DeleteOrphanedSealedFile(run.Id, attemptId, ArtifactPurpose.AgentContextManifest);
                RemoveTracked();
                return Result<CreateClaudeCriticalReviewAttemptCommandResult>.Failure(
                    Error.Failure("attempts.persistence_failed", "The attempt could not be durably recorded."));
            }

            return Result<CreateClaudeCriticalReviewAttemptCommandResult>.Success(
                new CreateClaudeCriticalReviewAttemptCommandResult(attemptId, attemptNumber));
        }
    }

    /// <summary>The independent read of the exact step/attempt association of a coordinator step (ADR-0055). Without a probe the
    /// outcome is unresolved, never assumed absent.</summary>
    private async Task<AttemptDurabilityCheckResult> ProbeStepAsync(PlanningSequenceStepTicket ticket, CancellationToken cancellationToken) =>
        planningStepProbe is null
            ? AttemptDurabilityCheckResult.Unresolved
            : await planningStepProbe.CheckAsync(ticket.StepId, cancellationToken) switch
            {
                PlanningSequenceStepProbeResult.Claimed => AttemptDurabilityCheckResult.Persisted,
                PlanningSequenceStepProbeResult.NotClaimed => AttemptDurabilityCheckResult.NotPersisted,
                _ => AttemptDurabilityCheckResult.Unresolved,
            };

    /// <summary>Rolls back the claim's own guard transaction and releases it, swallowing a failure from
    /// either step: whatever caused the caller's own primary failure already leaves this transaction's fate
    /// uncertain, and the <see cref="IAttemptDurabilityProbe"/> check each caller performs afterwards is the
    /// actual authority on durable state.</summary>
    private static async Task RollbackBestEffortAsync(IDbContextTransaction transaction, CancellationToken cancellationToken)
    {
        try
        {
            await transaction.RollbackAsync(cancellationToken);
        }
        catch (Exception)
        {
            // Deliberately broad: a rollback after an ambiguous or completed commit can throw a provider
            // exception or an InvalidOperationException depending on the transaction's own actual fate.
        }
        finally
        {
            try
            {
                await transaction.DisposeAsync();
            }
            catch (Exception)
            {
                // Best-effort release; the enclosing await using disposes again as a safe no-op.
            }
        }
    }

    private sealed record ReviewEligibility(CollaborationMessage? Message, Error? Error)
    {
        public static ReviewEligibility Succeeded(CollaborationMessage message) => new(message, null);

        public static ReviewEligibility Failed(Error error) => new(null, error);
    }
}
