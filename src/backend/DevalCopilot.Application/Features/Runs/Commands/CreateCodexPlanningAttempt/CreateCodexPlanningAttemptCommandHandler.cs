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

namespace DevalCopilot.Application.Features.Runs.Commands.CreateCodexPlanningAttempt;

public sealed class CreateCodexPlanningAttemptCommandHandler(
    IDevalCopilotDbContext dbContext,
    IGitWorkspaceEvidenceReader evidenceReader,
    IArtifactStore artifactStore,
    TimeProvider timeProvider)
    : ICommandHandler<CreateCodexPlanningAttemptCommand, Result<CreateCodexPlanningAttemptCommandResult>>
{
    /// <summary>Hard ceiling on the sealed context-manifest artifact — a bounded reference
    /// document, never a transcript or repository copy.</summary>
    private const int MaxContextManifestBytes = 16 * 1024;

    private static readonly TimeSpan InvocationTimeout = AgentClaimPathPolicy.GetInvocationTimeout(AgentClaimPath.CodexPlanning);
    private const int MaxBytesPerStream = 256 * 1024;
    private const int MaxTotalCapturedBytes = 512 * 1024;

    public async Task<Result<CreateCodexPlanningAttemptCommandResult>> HandleAsync(
        CreateCodexPlanningAttemptCommand command, CancellationToken cancellationToken)
    {
        var run = await dbContext.Runs.SingleOrDefaultAsync(candidate => candidate.Id == command.RunId, cancellationToken);
        if (run is null)
        {
            return Result<CreateCodexPlanningAttemptCommandResult>.Failure(
                Error.NotFound("runs.not_found", "The requested run was not found."));
        }

        if (run.Lifecycle is not (RunLifecycle.Created or RunLifecycle.Running))
        {
            return Result<CreateCodexPlanningAttemptCommandResult>.Failure(
                Error.Conflict("runs.not_active", $"The run is {run.Lifecycle} and cannot start a planning attempt."));
        }

        var workspace = await dbContext.GitWorkspaces
            .Where(candidate => candidate.ProjectId == run.ProjectId)
            .OrderByDescending(candidate => candidate.WorkspaceNumber)
            .FirstOrDefaultAsync(cancellationToken);
        if (workspace is null || workspace.Status != WorkspaceStatus.Ready)
        {
            return Result<CreateCodexPlanningAttemptCommandResult>.Failure(
                Error.Conflict("agent_attempts.workspace_not_ready", "A ready isolated workspace is required to request a Codex plan."));
        }

        var leaseIsActive = await dbContext.RepositoryMutationLeases
            .AnyAsync(lease => lease.WorkspaceId == workspace.Id && lease.Status == LeaseStatus.Active, cancellationToken);
        if (!leaseIsActive)
        {
            return Result<CreateCodexPlanningAttemptCommandResult>.Failure(
                Error.Conflict("agent_attempts.lease_not_active", "An active workspace lease is required to request a Codex plan."));
        }

        var checkpoint = await dbContext.GitCheckpoints
            .Where(candidate => candidate.WorkspaceId == workspace.Id)
            .OrderByDescending(candidate => candidate.CheckpointNumber)
            .FirstOrDefaultAsync(cancellationToken);
        if (checkpoint is null)
        {
            return Result<CreateCodexPlanningAttemptCommandResult>.Failure(
                Error.Conflict("agent_attempts.checkpoint_missing", "A current Git checkpoint is required to request a Codex plan."));
        }

        // Run-wide, not Agent-scoped: a Simulated or Process attempt already Running for this run
        // is exactly as disqualifying as an Agent attempt already Running — only one attempt of
        // any kind may ever be Running for a run at a time. The filtered unique index on
        // (RunId WHERE Status = 'Running') is the database backstop for the race this check
        // alone cannot close.
        var alreadyRunning = await dbContext.Attempts.AnyAsync(
            candidate => candidate.RunId == run.Id && candidate.Status == AttemptStatus.Running,
            cancellationToken);
        if (alreadyRunning)
        {
            return Result<CreateCodexPlanningAttemptCommandResult>.Failure(
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
            return Result<CreateCodexPlanningAttemptCommandResult>.Failure(
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
                return Result<CreateCodexPlanningAttemptCommandResult>.Failure(
                    Error.Failure("agent_attempts.time_budget_evidence_invalid", "This claim's reserved Agent invocation time could not be safely evaluated: prior Agent attempt invocation-time evidence is missing or invalid, or this claim's own reservation is not representable."));
            }

            var projectedAgentInvocationTime = AgentInvocationTimeReservation.ComputeProjectedReservation(reservedAgentInvocationTime.Value, InvocationTimeout);
            if (projectedAgentInvocationTime is null)
            {
                return Result<CreateCodexPlanningAttemptCommandResult>.Failure(
                    Error.Failure("agent_attempts.time_budget_evidence_invalid", "This claim's reserved Agent invocation time could not be safely evaluated: prior Agent attempt invocation-time evidence is missing or invalid, or this claim's own reservation is not representable."));
            }

            if (projectedAgentInvocationTime.Value > maximumAgentInvocationTime)
            {
                return Result<CreateCodexPlanningAttemptCommandResult>.Failure(
                    Error.Conflict("agent_attempts.time_budget_exceeded", "This run has reached its maximum reserved Agent invocation time."));
            }
        }

        var codexSnapshot = await dbContext.HostCapabilitySnapshots
            .SingleOrDefaultAsync(candidate => candidate.Capability == Capability.CodexCli, cancellationToken);
        if (codexSnapshot is null
            || codexSnapshot.ReasonCode != CapabilityProbeReason.None
            || codexSnapshot.LaunchKind is null
            || string.IsNullOrWhiteSpace(codexSnapshot.ResolvedExecutablePath))
        {
            return Result<CreateCodexPlanningAttemptCommandResult>.Failure(
                Error.Conflict("agent_attempts.provider_not_observed", "The Codex runtime is not currently observed as available."));
        }

        var evidence = await evidenceReader.CaptureAsync(workspace.WorkspacePath, cancellationToken);
        if (evidence.Outcome != GitWorkspaceEvidenceOutcome.Success || evidence.FingerprintSha256 != checkpoint.FingerprintSha256)
        {
            return Result<CreateCodexPlanningAttemptCommandResult>.Failure(
                Error.Conflict("agent_attempts.checkpoint_not_current", "The selected source checkpoint is no longer current for this workspace."));
        }

        var attemptId = Guid.NewGuid();
        var manifestArtifactId = Guid.NewGuid();
        var nowUtc = timeProvider.GetUtcNow();

        var manifestJson = ContextManifestBuilder.Build(
            run.ProjectId, workspace.Id, checkpoint.Id, checkpoint.FingerprintSha256, run.Objective, null, []);
        if (System.Text.Encoding.UTF8.GetByteCount(manifestJson) > MaxContextManifestBytes)
        {
            // Genuinely unreachable with today's bounded manifest fields, but never silently
            // truncated or persisted over the bound if a future field addition regresses this.
            return Result<CreateCodexPlanningAttemptCommandResult>.Failure(
                Error.Failure("agent_attempts.context_manifest_too_large", "The context manifest exceeds its bound."));
        }

        var manifestPartialPath = artifactStore.GetPartialPath(run.Id, attemptId, ArtifactPurpose.AgentContextManifest);
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPartialPath)!);
        await File.WriteAllTextAsync(manifestPartialPath, manifestJson, cancellationToken);
        var sealedManifest = await artifactStore.SealAsync(run.Id, attemptId, ArtifactPurpose.AgentContextManifest, cancellationToken);
        if (sealedManifest is null)
        {
            return Result<CreateCodexPlanningAttemptCommandResult>.Failure(
                Error.Failure("agent_attempts.context_manifest_seal_failed", "The context manifest could not be sealed."));
        }

        var attemptNumber = await dbContext.Attempts.Where(candidate => candidate.RunId == run.Id).CountAsync(cancellationToken) + 1;
        var agentBudgetSlot = agentAttemptsUsed + 1;

        var attempt = Attempt.ClaimAgent(
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
        dbContext.Attempts.Add(attempt);

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
                return Result<CreateCodexPlanningAttemptCommandResult>.Success(
                    new CreateCodexPlanningAttemptCommandResult(attemptId, attemptNumber));
            }

            // Never persisted: the sealed manifest artifact this request already wrote is
            // certain to never be referenced by any Artifact row, so it is cleaned up
            // regardless of what actually caused the failure. The two entities this call added
            // are also removed from tracking so this DbContext instance can never later
            // accidentally re-attempt to persist a known-failed insert if its scope continues.
            artifactStore.DeleteOrphanedSealedFile(run.Id, attemptId, ArtifactPurpose.AgentContextManifest);
            dbContext.Attempts.Remove(attempt);
            dbContext.Artifacts.Remove(manifestArtifact);

            var competingRunningAttemptExists = await dbContext.Attempts
                .AsNoTracking()
                .AnyAsync(candidate => candidate.RunId == run.Id && candidate.Status == AttemptStatus.Running, cancellationToken);
            if (competingRunningAttemptExists)
            {
                // The race the filtered unique index exists to close: a concurrent request
                // committed its own Running attempt for this run first.
                return Result<CreateCodexPlanningAttemptCommandResult>.Failure(
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
                    return Result<CreateCodexPlanningAttemptCommandResult>.Failure(
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
                        return Result<CreateCodexPlanningAttemptCommandResult>.Failure(
                            Error.Failure("agent_attempts.time_budget_evidence_invalid", "This claim's reserved Agent invocation time could not be safely evaluated: prior Agent attempt invocation-time evidence is missing or invalid, or this claim's own reservation is not representable."));
                    }

                    var projectedAgentInvocationTimeOnRace = AgentInvocationTimeReservation.ComputeProjectedReservation(reservedAgentInvocationTimeNow.Value, InvocationTimeout);
                    if (projectedAgentInvocationTimeOnRace is null)
                    {
                        return Result<CreateCodexPlanningAttemptCommandResult>.Failure(
                            Error.Failure("agent_attempts.time_budget_evidence_invalid", "This claim's reserved Agent invocation time could not be safely evaluated: prior Agent attempt invocation-time evidence is missing or invalid, or this claim's own reservation is not representable."));
                    }

                    if (projectedAgentInvocationTimeOnRace.Value > maximumAgentInvocationTimeOnRace)
                    {
                        return Result<CreateCodexPlanningAttemptCommandResult>.Failure(
                            Error.Conflict("agent_attempts.time_budget_exceeded", "This run has reached its maximum reserved Agent invocation time."));
                    }
                }

                return Result<CreateCodexPlanningAttemptCommandResult>.Failure(
                    Error.Conflict("agent_attempts.budget_slot_conflict", "A concurrent request already claimed this Agent attempt's budget slot; retry the request."));
            }

            // Not a race loss on any known invariant — no competing Running attempt and no
            // competing budget-slot claim exist for this run at all. Some other persistence
            // failure caused this (disk, corruption, an unrelated constraint); never report a
            // conflict that would falsely imply a race that never happened.
            return Result<CreateCodexPlanningAttemptCommandResult>.Failure(
                Error.Failure("attempts.persistence_failed", "The attempt could not be durably recorded."));
        }

        return Result<CreateCodexPlanningAttemptCommandResult>.Success(
            new CreateCodexPlanningAttemptCommandResult(attempt.Id, attempt.AttemptNumber));
    }
}
