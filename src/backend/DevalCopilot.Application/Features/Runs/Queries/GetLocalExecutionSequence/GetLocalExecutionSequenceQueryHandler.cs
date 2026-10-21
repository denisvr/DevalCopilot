using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionSequence;

public sealed class GetLocalExecutionSequenceQueryHandler(IDevalCopilotDbContext dbContext, TimeProvider timeProvider)
    : IQueryHandler<GetLocalExecutionSequenceQuery, Result<GetLocalExecutionSequenceQueryResult>>
{
    public async Task<Result<GetLocalExecutionSequenceQueryResult>> HandleAsync(
        GetLocalExecutionSequenceQuery query, CancellationToken cancellationToken)
    {
        var run = await dbContext.Runs.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == query.RunId, cancellationToken);
        if (run is null)
        {
            return Result<GetLocalExecutionSequenceQueryResult>.Failure(LocalExecutionSequenceErrors.NotFound());
        }

        var latestEventSequence = await dbContext.Events.AsNoTracking()
            .Where(runEvent => runEvent.RunId == run.Id)
            .MaxAsync(runEvent => (long?)runEvent.Sequence, cancellationToken) ?? 0;

        try
        {
            var sequence = await dbContext.LocalExecutionSequences.AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.RunId == run.Id, cancellationToken);
            if (sequence is null)
            {
                var released = await LocalExecutionOwnership.HasIncoherentLinkAsync(dbContext, run.ProjectId, cancellationToken)
                    ? LocalExecutionSequenceManualFence.Unavailable
                    : LocalExecutionSequenceManualFence.Released;
                return Result<GetLocalExecutionSequenceQueryResult>.Success(
                    new GetLocalExecutionSequenceQueryResult(run.Id, null, null, released, latestEventSequence));
            }

            var snapshot = await ReadSnapshotAsync(sequence, cancellationToken);
            return Result<GetLocalExecutionSequenceQueryResult>.Success(new GetLocalExecutionSequenceQueryResult(
                run.Id, snapshot.Snapshot.State, snapshot.Snapshot, snapshot.Fence, latestEventSequence));
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException)
        {
            // A stored value this build cannot materialize: the fence cannot be established and nothing is disclosed.
            return Result<GetLocalExecutionSequenceQueryResult>.Success(new GetLocalExecutionSequenceQueryResult(
                run.Id, null, null, LocalExecutionSequenceManualFence.Unavailable, latestEventSequence));
        }
    }

    private async Task<(LocalExecutionSequenceSnapshot Snapshot, LocalExecutionSequenceManualFence Fence)> ReadSnapshotAsync(
        LocalExecutionSequence sequence, CancellationToken cancellationToken)
    {
        var nowUtc = timeProvider.GetUtcNow();
        var steps = await dbContext.LocalExecutionSequenceSteps.AsNoTracking()
            .Where(candidate => candidate.SequenceId == sequence.Id)
            .OrderBy(candidate => candidate.Ordinal)
            .Take(LocalExecutionSequencePolicy.MaximumSteps(LocalExecutionSequencePolicy.MaximumRecipes) + 1)
            .ToListAsync(cancellationToken);
        if (steps.Count > LocalExecutionSequencePolicy.MaximumSteps(sequence.RecipeCount))
        {
            throw new InvalidOperationException("The recorded steps exceed the sequence bound.");
        }

        var attemptIds = steps.Where(step => step.IsAgent && step.OperationId != null).Select(step => step.OperationId!.Value).ToArray();
        var attempts = await dbContext.Attempts.AsNoTracking()
            .Where(candidate => attemptIds.Contains(candidate.Id))
            .ToDictionaryAsync(candidate => candidate.Id, cancellationToken);
        var executionIds = steps.Where(step => !step.IsAgent && step.OperationId != null).Select(step => step.OperationId!.Value).ToArray();
        var executions = await dbContext.VerificationExecutions.AsNoTracking()
            .Where(candidate => executionIds.Contains(candidate.Id))
            .ToDictionaryAsync(candidate => candidate.Id, cancellationToken);
        var recipeNames = await dbContext.LocalExecutionSequenceRecipes.AsNoTracking()
            .Where(recipe => recipe.SequenceId == sequence.Id)
            .ToDictionaryAsync(recipe => recipe.Ordinal, recipe => recipe.CommandName, cancellationToken);

        var receipts = steps.Select(step => ToReceipt(step, attempts, executions, recipeNames)).ToArray();
        var outstanding = receipts.FirstOrDefault(receipt => receipt.OperationId is not null
            && (receipt.OperationStatus == nameof(AttemptStatus.Running) || receipt.OperationStatus == nameof(VerificationExecutionStatus.Running)
                || receipt.Quarantined));

        var blocked = await LocalExecutionSequenceFence.IsRunBlockedAsync(dbContext, sequence.RunId, cancellationToken);
        var fence = await LocalExecutionOwnership.HasIncoherentLinkAsync(dbContext, sequence.ProjectId, cancellationToken)
            ? LocalExecutionSequenceManualFence.Unavailable
            : blocked ? LocalExecutionSequenceManualFence.Blocked : LocalExecutionSequenceManualFence.Released;

        // An elapsed deadline reads as attention immediately, before the coordinator sweep persists it. The recorded state is unchanged.
        var effectiveState = sequence.State;
        string? effectiveReason = sequence.ReasonCode;
        var observedBeforePersisted = false;
        var reserved = steps.FirstOrDefault(step => step.State == LocalExecutionSequenceStepState.Reserved);
        var claimed = steps.FirstOrDefault(step => step.State == LocalExecutionSequenceStepState.Claimed);
        var claimedReceipt = claimed is null ? null : receipts.First(receipt => receipt.StepId == claimed.Id);
        if (sequence.State is LocalExecutionSequenceState.Armed or LocalExecutionSequenceState.PreparingStep
            && nowUtc >= sequence.AdvancementDeadlineUtc)
        {
            effectiveState = LocalExecutionSequenceState.NeedsAttention;
            effectiveReason = LocalExecutionSequenceReason.AdvancementDeadlineElapsed;
            observedBeforePersisted = true;
        }
        else if (sequence.State == LocalExecutionSequenceState.PreparingStep && reserved is not null && nowUtc >= reserved.PreparationDeadlineUtc)
        {
            effectiveState = LocalExecutionSequenceState.NeedsAttention;
            effectiveReason = LocalExecutionSequenceReason.PreparationExpired;
            observedBeforePersisted = true;
        }
        else if (sequence.State is LocalExecutionSequenceState.AwaitingStepResult or LocalExecutionSequenceState.Stopping
                 && claimed?.ResultDeadlineUtc is { } deadline
                 && claimedReceipt is not null)
        {
            var terminal = claimedReceipt.OperationStatus is not null
                && claimedReceipt.OperationStatus != nameof(AttemptStatus.Running)
                && claimedReceipt.OperationStatus != nameof(VerificationExecutionStatus.Running);
            var completedAt = claimed.IsAgent
                ? attempts.GetValueOrDefault(claimed.OperationId ?? Guid.Empty)?.CompletedAtUtc
                : executions.GetValueOrDefault(claimed.OperationId ?? Guid.Empty)?.CompletedAtUtc;
            if ((!terminal && nowUtc >= deadline)
                || (terminal && sequence.State == LocalExecutionSequenceState.AwaitingStepResult && (completedAt ?? nowUtc) >= deadline))
            {
                // A genuinely late completion that the sweep has not yet judged can no longer authorize a successor. A Stop that won
                // keeps Stopping, because the sweep ends that as Stopped regardless of lateness.
                effectiveState = LocalExecutionSequenceState.NeedsAttention;
                effectiveReason = LocalExecutionSequenceReason.ResultDeadlineElapsed;
                observedBeforePersisted = true;
            }
        }

        var awaitingReconciliation = outstanding is not null && !LocalExecutionSequencePolicy.IsAuthorityLive(effectiveState);
        var last = steps.Count == 0 ? null : steps[^1];
        var final = sequence.State is LocalExecutionSequenceState.ReadyForHumanApproval or LocalExecutionSequenceState.HumanEscalation
            ? await ProveFinalReferencesAsync(sequence, steps, attempts, cancellationToken)
            : null;
        var snapshot = new LocalExecutionSequenceSnapshot(
            sequence.Id,
            sequence.PlanProposalMessageId,
            sequence.PlanAuthority,
            sequence.WorkspaceId,
            sequence.CheckpointId,
            sequence.FingerprintSha256,
            last?.SourceCheckpointId ?? sequence.CheckpointId,
            last?.SourceFingerprintSha256 ?? sequence.FingerprintSha256,
            effectiveState,
            sequence.State,
            observedBeforePersisted,
            effectiveReason,
            sequence.DetailCode,
            LocalExecutionSequenceNarrative.NextActionOf(effectiveState, awaitingReconciliation),
            sequence.Revision,
            sequence.RecipeCount,
            LocalExecutionSequencePolicy.MaximumSteps(sequence.RecipeCount),
            sequence.CorrectionAllowance,
            steps.Count(step => LocalExecutionSequencePolicy.IsCorrectionKind(step.Kind) && step.State != LocalExecutionSequenceStepState.Revoked),
            steps.Where(step => step.Kind == LocalExecutionStepKind.Verification && step.State != LocalExecutionSequenceStepState.Revoked)
                .Select(step => step.PassNumber).Distinct().Count(),
            steps.Count(step => step.IsAgent && step.State != LocalExecutionSequenceStepState.Revoked),
            sequence.ArmedAtUtc,
            sequence.AdvancementDeadlineUtc,
            sequence.UpdatedAtUtc,
            sequence.StopRequestedAtUtc,
            sequence.EndedAtUtc,
            awaitingReconciliation,
            outstanding?.OperationId,
            receipts,
            final);
        return (snapshot, fence);
    }

    private static LocalExecutionSequenceStepReceipt ToReceipt(
        LocalExecutionSequenceStep step,
        IReadOnlyDictionary<Guid, Attempt> attempts,
        IReadOnlyDictionary<Guid, VerificationExecution> executions,
        IReadOnlyDictionary<int, string> recipeNames)
    {
        string? status = null;
        string? outcome = null;
        bool? dispatched = null;
        bool? exitRecorded = null;
        var quarantined = false;
        if (step.OperationId is { } operationId)
        {
            if (step.IsAgent && attempts.TryGetValue(operationId, out var attempt))
            {
                status = attempt.Status.ToString();
                outcome = attempt.AgentOutcome?.ToString();
                dispatched = attempt.AgentDispatchedAtUtc.HasValue;
                exitRecorded = LocalExecutionExitProof.Holds(attempt);
                quarantined = attempt.AgentDispatchedAtUtc.HasValue && attempt.Status != AttemptStatus.Running && exitRecorded == false;
            }
            else if (!step.IsAgent && executions.TryGetValue(operationId, out var execution))
            {
                status = execution.Status.ToString();
                outcome = execution.Outcome?.ToString();
                dispatched = execution.DispatchedAtUtc.HasValue;
                exitRecorded = LocalExecutionExitProof.Holds(execution);
                quarantined = execution.DispatchedAtUtc.HasValue && execution.Status != VerificationExecutionStatus.Running && exitRecorded == false;
            }
        }

        return new LocalExecutionSequenceStepReceipt(
            step.Id,
            step.Ordinal,
            step.Kind,
            step.PassNumber,
            step.RecipeOrdinal,
            step.RecipeOrdinal is { } ordinal ? recipeNames.GetValueOrDefault(ordinal) : null,
            step.State,
            step.ReasonCode,
            step.OperationId,
            status,
            outcome,
            dispatched,
            exitRecorded,
            quarantined,
            step.PreparationReservedAtUtc,
            step.PreparationDeadlineUtc,
            step.ClaimedAtUtc,
            step.ResultDeadlineUtc);
    }

    /// <summary>The exact final references, proved from the linked rows: an approved review is a linked CodeReview step whose attempt
    /// recorded ReviewApproved for the named report; an escalation is a message that answers the named report. Any mismatch discloses
    /// no reference.</summary>
    private async Task<LocalExecutionSequenceFinalReferences?> ProveFinalReferencesAsync(
        LocalExecutionSequence sequence,
        IReadOnlyList<LocalExecutionSequenceStep> steps,
        IReadOnlyDictionary<Guid, Attempt> attempts,
        CancellationToken cancellationToken)
    {
        if (sequence.FinalReportMessageId is not { } reportId)
        {
            return null;
        }

        if (sequence.State == LocalExecutionSequenceState.ReadyForHumanApproval && sequence.FinalReviewAttemptId is { } reviewId)
        {
            var reviewStep = steps.FirstOrDefault(step => step.Kind == LocalExecutionStepKind.CodeReview
                && step.OperationId == reviewId
                && step.ReportMessageId == reportId);
            var proved = reviewStep is not null
                && attempts.TryGetValue(reviewId, out var review)
                && await LocalExecutionFinalEvidence.ApprovalHoldsAsync(dbContext, sequence, reviewStep, review, cancellationToken);
            return proved ? new LocalExecutionSequenceFinalReferences(reportId, reviewId, null) : null;
        }

        if (sequence.State == LocalExecutionSequenceState.HumanEscalation && sequence.FinalEscalationMessageId is { } escalationId)
        {
            var proved = false;
            if (sequence.ReasonCode == LocalExecutionSequenceReason.AllowanceReached)
            {
                proved = await LocalExecutionFinalEvidence.HostEscalationHoldsAsync(
                    dbContext, sequence, steps, escalationId, reportId, cancellationToken);
            }
            else if (sequence.ReasonCode == LocalExecutionSequenceReason.DiagnosisEscalated)
            {
                var diagnosisStep = steps.FirstOrDefault(step => step.Kind == LocalExecutionStepKind.VerificationDiagnosis
                    && step.ReportMessageId == reportId
                    && step.OperationId is not null);
                proved = diagnosisStep is not null
                    && attempts.TryGetValue(diagnosisStep.OperationId!.Value, out var diagnosis)
                    && await LocalExecutionFinalEvidence.ProviderEscalationHoldsAsync(
                        dbContext, sequence, diagnosisStep, diagnosis, escalationId, cancellationToken);
            }

            return proved ? new LocalExecutionSequenceFinalReferences(reportId, null, escalationId) : null;
        }

        return null;
    }
}
