using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Policies.Abandonment;
using DevalCopilot.Application.Features.Runs.Policies.PlanningSequences;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetPlanningSequence;

public sealed class GetPlanningSequenceQueryHandler(IDevalCopilotDbContext dbContext, TimeProvider timeProvider)
    : IQueryHandler<GetPlanningSequenceQuery, Result<GetPlanningSequenceQueryResult>>
{
    public async Task<Result<GetPlanningSequenceQueryResult>> HandleAsync(
        GetPlanningSequenceQuery query, CancellationToken cancellationToken)
    {
        var run = await dbContext.Runs.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == query.RunId, cancellationToken);
        if (run is null)
        {
            return Result<GetPlanningSequenceQueryResult>.Failure(PlanningSequenceErrors.NotFound());
        }

        var latestEventSequence = await dbContext.Events.AsNoTracking()
            .Where(runEvent => runEvent.RunId == run.Id)
            .MaxAsync(runEvent => (long?)runEvent.Sequence, cancellationToken) ?? 0;

        PlanningSequence? sequence;
        try
        {
            sequence = await dbContext.PlanningSequences.AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.RunId == run.Id, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            // A stored value this build cannot materialize: the fence cannot be established and nothing is disclosed.
            return Result<GetPlanningSequenceQueryResult>.Success(new GetPlanningSequenceQueryResult(
                run.Id, null, null, null, PlanningSequenceManualFence.Unavailable, latestEventSequence));
        }

        if (sequence is null)
        {
            return Result<GetPlanningSequenceQueryResult>.Success(new GetPlanningSequenceQueryResult(
                run.Id, null, null, await ReadArmingAvailabilityAsync(run, cancellationToken), PlanningSequenceManualFence.Released,
                latestEventSequence));
        }

        try
        {
            var snapshot = await ReadSnapshotAsync(run, sequence, cancellationToken);
            return Result<GetPlanningSequenceQueryResult>.Success(new GetPlanningSequenceQueryResult(
                run.Id, snapshot.Snapshot.State, snapshot.Snapshot, null, snapshot.Fence, latestEventSequence));
        }
        catch (InvalidOperationException)
        {
            return Result<GetPlanningSequenceQueryResult>.Success(new GetPlanningSequenceQueryResult(
                run.Id, null, null, null, PlanningSequenceManualFence.Unavailable, latestEventSequence));
        }
    }

    private async Task<(PlanningSequenceSnapshot Snapshot, PlanningSequenceManualFence Fence)> ReadSnapshotAsync(
        Run run, PlanningSequence sequence, CancellationToken cancellationToken)
    {
        var nowUtc = timeProvider.GetUtcNow();
        var steps = await dbContext.PlanningSequenceSteps.AsNoTracking()
            .Where(candidate => candidate.SequenceId == sequence.Id)
            .OrderBy(candidate => candidate.Ordinal)
            .ToListAsync(cancellationToken);
        var attemptIds = steps.Where(step => step.AttemptId is not null).Select(step => step.AttemptId!.Value).ToArray();
        var attempts = await dbContext.Attempts.AsNoTracking()
            .Where(candidate => attemptIds.Contains(candidate.Id))
            .ToDictionaryAsync(candidate => candidate.Id, cancellationToken);

        var receipts = steps.Select(step => ToReceipt(step, step.AttemptId is { } id && attempts.TryGetValue(id, out var attempt) ? attempt : null))
            .ToArray();

        // The linked attempt that is still Running is unresolved: it keeps the fence and stays visible as awaiting reconciliation.
        var outstanding = steps
            .Select(step => attempts.GetValueOrDefault(step.AttemptId ?? Guid.Empty))
            .FirstOrDefault(attempt => attempt is { Status: AttemptStatus.Running });
        var fence = sequence.IsAuthorityLive || outstanding is not null
            ? PlanningSequenceManualFence.Blocked
            : PlanningSequenceManualFence.Released;

        // An elapsed deadline reads as attention immediately, before the coordinator sweep persists it. The recorded state is unchanged.
        var effectiveState = sequence.State;
        string? effectiveReason = sequence.ReasonCode;
        var observedBeforePersisted = false;
        var reserved = steps.FirstOrDefault(step => step.State == PlanningSequenceStepState.Reserved);
        var claimedAttempt = steps
            .Where(step => step.State == PlanningSequenceStepState.Claimed)
            .Select(step => attempts.GetValueOrDefault(step.AttemptId ?? Guid.Empty))
            .FirstOrDefault();
        if (sequence.State == PlanningSequenceState.PreparingStep && reserved is not null && nowUtc >= reserved.PreparationDeadlineUtc)
        {
            effectiveState = PlanningSequenceState.NeedsAttention;
            effectiveReason = PlanningSequenceReason.PreparationExpired;
            observedBeforePersisted = true;
        }
        else if (sequence.State is PlanningSequenceState.AwaitingStepResult or PlanningSequenceState.Stopping
                 && claimedAttempt is { Status: AttemptStatus.Running }
                 && PlanningSequencePolicy.IsResultLate(claimedAttempt.ClaimedAtUtc, claimedAttempt.AgentTimeout, nowUtc))
        {
            effectiveState = PlanningSequenceState.NeedsAttention;
            effectiveReason = PlanningSequenceReason.ResultDeadlineElapsed;
            observedBeforePersisted = true;
        }
        else if (sequence.State == PlanningSequenceState.AwaitingStepResult
                 && claimedAttempt is { Status: AttemptStatus.Completed }
                 && PlanningSequencePolicy.IsResultLate(
                     claimedAttempt.ClaimedAtUtc, claimedAttempt.AgentTimeout, claimedAttempt.CompletedAtUtc ?? nowUtc))
        {
            // A genuinely late completion that the sweep has not yet judged: it can no longer authorize a successor, so it already
            // reads as attention. A Stop that won keeps Stopping, because the sweep ends that as Stopped regardless of lateness.
            effectiveState = PlanningSequenceState.NeedsAttention;
            effectiveReason = PlanningSequenceReason.ResultDeadlineElapsed;
            observedBeforePersisted = true;
        }

        var final = sequence.State is PlanningSequenceState.ReadyForImplementation or PlanningSequenceState.AwaitingHumanDecision
            ? await ProveFinalReferencesAsync(sequence, steps, attempts, cancellationToken)
            : null;

        var snapshot = new PlanningSequenceSnapshot(
            sequence.Id,
            sequence.WorkspaceId,
            sequence.CheckpointId,
            sequence.FingerprintSha256,
            effectiveState,
            sequence.State,
            observedBeforePersisted,
            effectiveReason,
            sequence.DetailCode,
            PlanningSequenceNarrative.NextActionOf(effectiveState),
            sequence.Revision,
            sequence.ArmedAtUtc,
            sequence.UpdatedAtUtc,
            sequence.StopRequestedAtUtc,
            sequence.EndedAtUtc,
            outstanding is not null && !PlanningSequencePolicy.IsAuthorityLive(effectiveState),
            outstanding?.Id,
            receipts,
            final);
        return (snapshot, fence);
    }

    private static PlanningSequenceStepReceipt ToReceipt(PlanningSequenceStep step, Attempt? attempt) => new(
        step.Id,
        step.Ordinal,
        step.Role,
        PlanningSequencePolicy.ProviderOf(step.Ordinal),
        step.State,
        step.ReasonCode,
        step.AttemptId,
        attempt?.AttemptNumber,
        attempt?.Status,
        attempt?.AgentOutcome,
        attempt is null ? null : attempt.AgentDispatchedAtUtc.HasValue,
        step.PreparationReservedAtUtc,
        step.PreparationDeadlineUtc,
        attempt?.ClaimedAtUtc,
        attempt is null ? null : PlanningSequencePolicy.ResultDeadlineOf(attempt.ClaimedAtUtc, attempt.AgentTimeout));

    /// <summary>The final plan or escalation, proved by judging the last concluded step through the same classifier the coordinator used.
    /// A mismatch between the recorded end state and the proved verdict discloses no reference.</summary>
    private async Task<PlanningSequenceFinalReferences?> ProveFinalReferencesAsync(
        PlanningSequence sequence,
        IReadOnlyList<PlanningSequenceStep> steps,
        IReadOnlyDictionary<Guid, Attempt> attempts,
        CancellationToken cancellationToken)
    {
        var last = steps.LastOrDefault(step => step.State == PlanningSequenceStepState.Concluded);
        if (last?.AttemptId is not { } attemptId || !attempts.TryGetValue(attemptId, out var attempt))
        {
            return null;
        }

        var snapshot = await PlanningLineage.TryLoadSnapshotAsync(dbContext, sequence.RunId, cancellationToken);
        if (snapshot is null)
        {
            return null;
        }

        var verdict = PlanningSequenceOutcomeClassifier.Classify(snapshot, sequence, last, attempt);
        return (sequence.State, verdict) switch
        {
            (PlanningSequenceState.ReadyForImplementation, { Kind: PlanningSequenceVerdictKind.Accepted } accepted) =>
                new PlanningSequenceFinalReferences(accepted.FinalProposalMessageId!.Value, accepted.ClosingMessageId, null, attemptId),
            (PlanningSequenceState.AwaitingHumanDecision, { Kind: PlanningSequenceVerdictKind.Escalated } escalated) =>
                new PlanningSequenceFinalReferences(escalated.FinalProposalMessageId!.Value, null, escalated.ClosingMessageId, attemptId),
            _ => null,
        };
    }

    /// <summary>The recorded admission facts only: no Git observation. Either the exact candidate tuple or one fixed refusal code, in the
    /// same order the arming command decides.</summary>
    private async Task<PlanningSequenceArmingAvailability> ReadArmingAvailabilityAsync(Run run, CancellationToken cancellationToken)
    {
        static PlanningSequenceArmingAvailability Refused(string code) => new(false, code, null, null, null);

        if (await CurrentRunExecutionMode.ReadAsync(dbContext, run.Id, cancellationToken) != RunExecutionMode.ManualAgent)
        {
            return Refused(PlanningSequenceErrors.RunNotManualCode);
        }

        var pristine = run.Lifecycle == RunLifecycle.Created
            && !await dbContext.Attempts.AsNoTracking().AnyAsync(candidate => candidate.RunId == run.Id, cancellationToken)
            && !await dbContext.CollaborationMessages.AsNoTracking().AnyAsync(candidate => candidate.RunId == run.Id, cancellationToken)
            && !await dbContext.LocalCommitOperations.AsNoTracking().AnyAsync(candidate => candidate.RunId == run.Id, cancellationToken);
        if (!pristine)
        {
            return Refused(PlanningSequenceErrors.RunNotPristineCode);
        }

        if (await RunAbandonmentAuthority.FindBlockerAsync(dbContext, run.ProjectId, cancellationToken) is not null)
        {
            return Refused(PlanningSequenceErrors.ProjectBusyCode);
        }

        var workspace = await dbContext.GitWorkspaces.AsNoTracking()
            .Where(candidate => candidate.ProjectId == run.ProjectId)
            .OrderByDescending(candidate => candidate.WorkspaceNumber)
            .FirstOrDefaultAsync(cancellationToken);
        if (workspace is null || workspace.Status != WorkspaceStatus.Ready)
        {
            return Refused(PlanningSequenceErrors.WorkspaceNotReadyCode);
        }

        if (!await dbContext.RepositoryMutationLeases.AsNoTracking().AnyAsync(
                lease => lease.WorkspaceId == workspace.Id && lease.Status == LeaseStatus.Active, cancellationToken))
        {
            return Refused(PlanningSequenceErrors.LeaseNotActiveCode);
        }

        var checkpoint = await dbContext.GitCheckpoints.AsNoTracking()
            .Where(candidate => candidate.WorkspaceId == workspace.Id)
            .OrderByDescending(candidate => candidate.CheckpointNumber)
            .FirstOrDefaultAsync(cancellationToken);
        return checkpoint is null
            ? Refused(PlanningSequenceErrors.CheckpointMissingCode)
            : new PlanningSequenceArmingAvailability(true, null, workspace.Id, checkpoint.Id, checkpoint.FingerprintSha256);
    }
}
